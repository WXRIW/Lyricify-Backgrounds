using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using NAudioComplex = NAudio.Dsp.Complex;

#nullable disable
namespace Lyricify.Backgrounds.AppleMusicInspired.Rendering
{
    /// <summary>
    /// Captures loopback audio and produces smoothed spectrum values for the
    /// animated background.
    /// </summary>
    public sealed class AppleMusicSpectrumAnalysis
    {
        private const int FftExponent = 12;
        private const int FftLength = 1 << FftExponent;
        private const int FastFftExponent = 11;
        private const int FastFftLength = 1 << FastFftExponent;
        private const int AnalysisReportsPerSecond = 120;
        private const int CaptureBufferMilliseconds = 10;
        private const double MissingReportSeconds = 0.25;
        private const double CaptureRestartDelaySeconds = 1.0;

        // Weight the four most recent samples from oldest to newest.
        private static readonly float[] SpectrumSampleRamp =
            { 0.1f, 0.2f, 0.3f, 0.4f };
        private static readonly Vector4 SpectrumTargetDecay =
            new(0.98f, 0.99f, 0.999f, 0f);
        private const float SpectrumPowerFollow = 0.5f;
        private const float FeatureBaselineAttackSeconds = 1.1f;
        private const float FeatureBaselineReleaseSeconds = 0.16f;
        private const int ConfirmationReportCount = 3;
        private const float ImmediateTriggerResponse = 0.72f;
        private const float ImmediateTriggerSupportResponse = 0.32f;
        private const float SilenceFloorDecibels = -72f;
        private const float BassLevelFloorDecibels = -50f;
        private const float BassLevelCeilingDecibels = -18f;
        private const float BassDominanceFloorDecibels = 0f;
        private const float BassDominanceCeilingDecibels = 8f;
        private const float BassRiseFloorDecibels = 1.2f;
        private const float BassRiseCeilingDecibels = 7f;
        private const float SharpAttackLevelFloorDecibels = -45f;
        private const float SharpAttackRiseFloorDecibels = 7f;
        private const float SharpAttackRiseCeilingDecibels = 14f;
        private const float SharpAttackReleaseSeconds = 0.09f;
        private const float HarmonicBassConfidenceFloor = 0.12f;
        private const float HarmonicBassConfidenceCeiling = 0.3f;
        private const float HarmonicBassAttackBoost = 0.9f;
        private const float SustainedBassResponse = 0.1f;

        private static readonly Guid PcmSubFormat =
            new("00000001-0000-0010-8000-00aa00389b71");
        private static readonly Guid IeeeFloatSubFormat =
            new("00000003-0000-0010-8000-00aa00389b71");
        // Blend the first two processed spectrum lanes before scaling.
        private const float ImagePulsePowerMix = 0.1f;
        private const float ImagePulseIntensity = 0.33f;

        // Overlapping trigger bands cover deep kicks and higher bass notes.
        private static readonly FrequencyBand LowBassBand = new(30f, 105f);
        private static readonly FrequencyBand BassNoteBand = new(75f, 155f);
        private static readonly FrequencyBand UpperBassBand = new(145f, 210f);
        private static readonly FrequencyBand LowMidReferenceBand = new(155f, 380f);
        private static readonly FrequencyBand MidReferenceBand = new(380f, 760f);

        private readonly object _captureGate = new();
        private readonly object _analysisGate = new();
        private readonly object _spectrumSampleGate = new();
        private readonly float[] _sampleRing = new float[FftLength];
        private readonly NAudioComplex[] _fftBuffer = new NAudioComplex[FftLength];
        private readonly float[] _hannWindow = new float[FftLength];
        private readonly NAudioComplex[] _fastFftBuffer =
            new NAudioComplex[FastFftLength];
        private readonly float[] _fastHannWindow = new float[FastFftLength];
        private readonly BassTransientDetector _bassTransientDetector = new();
        private readonly PriorityQueue<Vector4, long> _delayedReports = new();
        private readonly float[] _unprocessedReportHistory =
            new float[ConfirmationReportCount];
        private readonly Vector4[] _recentSpectrumSamples =
            new Vector4[SpectrumSampleRamp.Length];

        private LowLatencyLoopbackCapture _capture;
        private bool _captureRequested;
        private int _captureGeneration;
        private int _sampleWriteIndex;
        private int _availableSampleCount;
        private int _samplesSinceAnalysis;
        private int _unprocessedReportWriteIndex;
        private int _availableUnprocessedReportCount;
        private int _recentSpectrumSampleWriteIndex;
        private int _sampleRate = 48000;

        private float _reportedPowerX;
        private float _reportedPowerY;
        private float _reportedPowerZ;
        private float _reportedPowerW;
        private long _lastReportTimestamp;

        private Vector4 _power;
        private Vector4 _unprocessed;
        private Vector4 _targetPower;
        private BassFeatureState _featureState;
        private BassFeatureState _fastFeatureState;
        private BassFeatureState _transientFeatureState;

        private readonly Func<int> _deviceLatencyProvider;

        public AppleMusicSpectrumAnalysis(Func<int> deviceLatencyProvider = null)
        {
            _deviceLatencyProvider = deviceLatencyProvider ?? (() => 0);
            FillHannWindow(_hannWindow);
            FillHannWindow(_fastHannWindow);
        }

        private static void FillHannWindow(float[] window)
        {
            for (int index = 0; index < window.Length; index++)
            {
                window[index] = 0.5f -
                    0.5f * MathF.Cos(2f * MathF.PI * index / (window.Length - 1));
            }
        }

        public void Start()
        {
            int generation;
            lock (_captureGate)
            {
                if (_captureRequested)
                {
                    return;
                }

                _captureRequested = true;
                generation = ++_captureGeneration;
            }

            TryStartCapture(generation);
        }

        public void Stop()
        {
            LowLatencyLoopbackCapture capture;
            lock (_captureGate)
            {
                _captureRequested = false;
                _captureGeneration++;
                capture = _capture;
                _capture = null;
                if (capture != null)
                {
                    capture.DataAvailable -= OnDataAvailable;
                    capture.RecordingStopped -= OnRecordingStopped;
                }
            }

            if (capture != null)
            {
                try
                {
                    capture.StopRecording();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
                finally
                {
                    DisposeCapture(capture);
                }
            }

            ResetAnalysis();
        }

        public Vector4 GetImageScales(bool isPlaying, float pulseScale = 1f)
        {
            long now = Stopwatch.GetTimestamp();
            long lastReport = Volatile.Read(ref _lastReportTimestamp);
            bool reportIsCurrent = lastReport != 0 &&
                (now - lastReport) / (double)Stopwatch.Frequency <= MissingReportSeconds;
            _unprocessed = isPlaying && reportIsCurrent
                ? new Vector4(
                    Volatile.Read(ref _reportedPowerX),
                    Volatile.Read(ref _reportedPowerY),
                    Volatile.Read(ref _reportedPowerZ),
                    Volatile.Read(ref _reportedPowerW))
                : Vector4.Zero;

            UpdateSpectrumPower(isPlaying && reportIsCurrent);
            if (_power.LengthSquared() < 0.00000001f)
            {
                _power = Vector4.Zero;
            }

            float effectivePulseScale = float.IsFinite(pulseScale)
                ? Math.Clamp(pulseScale, 0f, 10f)
                : 1f;

            // Clamp and smooth each lane before deriving a shared image scale.
            float processedPowerX = ProcessSpectrumPower(_power.X);
            float processedPowerY = ProcessSpectrumPower(_power.Y);
            float blendedPower = Lerp(
                processedPowerX,
                processedPowerY,
                ImagePulsePowerMix);
            float imageScale = 1f +
                ImagePulseIntensity * blendedPower * blendedPower *
                effectivePulseScale;

            return new Vector4(imageScale, imageScale, imageScale, 1f);
        }

        private void PushRecentSpectrumSample(Vector4 sample)
        {
            lock (_spectrumSampleGate)
            {
                _recentSpectrumSamples[_recentSpectrumSampleWriteIndex] = sample;
                _recentSpectrumSampleWriteIndex =
                    (_recentSpectrumSampleWriteIndex + 1) %
                    _recentSpectrumSamples.Length;
            }
        }

        private void UpdateSpectrumPower(bool useRecentSamples)
        {
            Vector4 weightedSamples = Vector4.Zero;
            if (useRecentSamples)
            {
                lock (_spectrumSampleGate)
                {
                    for (int index = 0;
                        index < _recentSpectrumSamples.Length;
                        index++)
                    {
                        int sampleIndex =
                            (_recentSpectrumSampleWriteIndex + index) %
                            _recentSpectrumSamples.Length;
                        weightedSamples +=
                            _recentSpectrumSamples[sampleIndex] *
                            SpectrumSampleRamp[index];
                    }
                }
            }

            _targetPower = Vector4.Max(
                weightedSamples,
                _targetPower * SpectrumTargetDecay);
            _power += (_targetPower - _power) * SpectrumPowerFollow;
        }

        private static float ProcessSpectrumPower(float power)
        {
            float x = Math.Clamp(power, 0f, 1f);
            return x * x * x * (x * (x * 6f - 15f) + 10f);
        }

        private static float Lerp(float from, float to, float amount)
        {
            return from + (to - from) * amount;
        }

        private void TryStartCapture(int generation)
        {
            LowLatencyLoopbackCapture capture = null;
            try
            {
                capture = new LowLatencyLoopbackCapture();
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;

                lock (_captureGate)
                {
                    if (!_captureRequested || generation != _captureGeneration || _capture != null)
                    {
                        capture.DataAvailable -= OnDataAvailable;
                        capture.RecordingStopped -= OnRecordingStopped;
                        DisposeCapture(capture);
                        return;
                    }

                    _capture = capture;
                    lock (_analysisGate)
                    {
                        _sampleRate = Math.Max(1, capture.WaveFormat.SampleRate);
                    }
                }

                capture.StartRecording();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                lock (_captureGate)
                {
                    if (ReferenceEquals(_capture, capture))
                    {
                        _capture = null;
                    }
                }

                if (capture != null)
                {
                    capture.DataAvailable -= OnDataAvailable;
                    capture.RecordingStopped -= OnRecordingStopped;
                    DisposeCapture(capture);
                }
                ScheduleCaptureRestart(generation);
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Debug.WriteLine(e.Exception);
            }

            if (sender is not LowLatencyLoopbackCapture stoppedCapture)
            {
                return;
            }

            int generation;
            bool shouldRestart;
            lock (_captureGate)
            {
                if (!ReferenceEquals(_capture, stoppedCapture))
                {
                    return;
                }

                _capture = null;
                stoppedCapture.DataAvailable -= OnDataAvailable;
                stoppedCapture.RecordingStopped -= OnRecordingStopped;
                generation = _captureGeneration;
                shouldRestart = _captureRequested;
            }

            DisposeCapture(stoppedCapture);
            ResetAnalysis();
            if (shouldRestart)
            {
                ScheduleCaptureRestart(generation);
            }
        }

        private static void DisposeCapture(LowLatencyLoopbackCapture capture)
        {
            try
            {
                capture.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void ScheduleCaptureRestart(int generation)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(CaptureRestartDelaySeconds));
                lock (_captureGate)
                {
                    if (!_captureRequested || generation != _captureGeneration || _capture != null)
                    {
                        return;
                    }
                }
                TryStartCapture(generation);
            });
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (sender is not LowLatencyLoopbackCapture capture || e.BytesRecorded <= 0)
            {
                return;
            }

            try
            {
                ProcessAudio(e.Buffer, e.BytesRecorded, capture.WaveFormat);
            }
            catch (Exception ex)
            {
                // Audio device/format changes must never take down the lyrics UI.
                Debug.WriteLine(ex);
            }
        }

        private void ProcessAudio(byte[] buffer, int byteCount, WaveFormat format)
        {
            int channels = Math.Max(1, format.Channels);
            int blockAlign = format.BlockAlign;
            if (blockAlign <= 0 || byteCount < blockAlign)
            {
                return;
            }

            int channelStride = blockAlign / channels;
            if (channelStride <= 0)
            {
                return;
            }

            SampleEncoding encoding = GetSampleEncoding(format);
            if (encoding == SampleEncoding.Unsupported)
            {
                return;
            }

            lock (_analysisGate)
            {
                _sampleRate = Math.Max(1, format.SampleRate);
                int completeByteCount = Math.Min(byteCount, buffer.Length) / blockAlign * blockAlign;
                for (int frameOffset = 0; frameOffset < completeByteCount; frameOffset += blockAlign)
                {
                    float mono = 0f;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        mono += ReadSample(
                            buffer,
                            frameOffset + channel * channelStride,
                            encoding);
                    }
                    PushSample(float.IsFinite(mono) ? mono / channels : 0f);
                }
            }
        }

        private void PushSample(float sample)
        {
            _bassTransientDetector.PushSample(sample, _sampleRate);
            _sampleRing[_sampleWriteIndex] = Math.Clamp(sample, -4f, 4f);
            _sampleWriteIndex = (_sampleWriteIndex + 1) % FftLength;
            _availableSampleCount = Math.Min(_availableSampleCount + 1, FftLength);
            _samplesSinceAnalysis++;

            int reportHopLength = Math.Max(1, _sampleRate / AnalysisReportsPerSecond);
            if (_availableSampleCount < FastFftLength ||
                _samplesSinceAnalysis < reportHopLength)
            {
                return;
            }

            _samplesSinceAnalysis = 0;
            AnalyzeSpectrum();
        }

        private void AnalyzeSpectrum()
        {
            int fastReadIndex =
                (_sampleWriteIndex - FastFftLength + FftLength) % FftLength;
            for (int index = 0; index < FastFftLength; index++)
            {
                _fastFftBuffer[index].X =
                    _sampleRing[fastReadIndex] * _fastHannWindow[index];
                _fastFftBuffer[index].Y = 0f;
                fastReadIndex = (fastReadIndex + 1) % FftLength;
            }
            FastFourierTransform.FFT(true, FastFftExponent, _fastFftBuffer);
            float fastBassResponse = GetBassResponse(
                _fastFftBuffer,
                FastFftLength,
                ref _fastFeatureState);

            float preciseBassResponse = 0f;
            if (_availableSampleCount >= FftLength)
            {
                int readIndex = _sampleWriteIndex;
                for (int index = 0; index < FftLength; index++)
                {
                    _fftBuffer[index].X =
                        _sampleRing[readIndex] * _hannWindow[index];
                    _fftBuffer[index].Y = 0f;
                    readIndex = (readIndex + 1) % FftLength;
                }
                FastFourierTransform.FFT(true, FftExponent, _fftBuffer);
                preciseBassResponse = GetBassResponse(
                    _fftBuffer,
                    FftLength,
                    ref _featureState);
            }

            _bassTransientDetector.GetBandPowers(
                out double transientBassPower,
                out double transientReferencePower);
            float transientBassResponse = GetBassResponseFromPowers(
                transientBassPower,
                transientReferencePower,
                ref _transientFeatureState);

            // Combine fast envelope response with short- and long-window FFTs.
            float unprocessedBassResponse = MathF.Max(
                transientBassResponse,
                MathF.Max(fastBassResponse, preciseBassResponse));
            bool allowImmediateTrigger = ShouldTriggerImmediately(
                transientBassResponse,
                fastBassResponse);
            float bassResponse = GetConfirmedResponse(
                unprocessedBassResponse,
                allowImmediateTrigger);
            Vector4 report = new(
                bassResponse,
                bassResponse,
                bassResponse,
                0f);

            // Schedule delayed reports against the monotonic clock.
            long now = Stopwatch.GetTimestamp();
            _delayedReports.Enqueue(
                report,
                GetReportDueTimestamp(now));
            if (!_delayedReports.TryPeek(
                    out _,
                    out long dueTimestamp) ||
                dueTimestamp > now)
            {
                return;
            }

            Vector4 delayedReport = Vector4.Zero;
            do
            {
                _delayedReports.TryDequeue(
                    out delayedReport,
                    out _);
            }
            while (_delayedReports.TryPeek(
                    out _,
                    out dueTimestamp) &&
                dueTimestamp <= now);

            Volatile.Write(ref _reportedPowerX, delayedReport.X);
            Volatile.Write(ref _reportedPowerY, delayedReport.Y);
            Volatile.Write(ref _reportedPowerZ, delayedReport.Z);
            Volatile.Write(ref _reportedPowerW, delayedReport.W);
            Volatile.Write(ref _lastReportTimestamp, now);
            PushRecentSpectrumSample(delayedReport);
        }

        private static bool ShouldTriggerImmediately(
            float transientResponse,
            float fastResponse)
        {
            return transientResponse >= ImmediateTriggerResponse &&
                    fastResponse >= ImmediateTriggerSupportResponse ||
                fastResponse >= ImmediateTriggerResponse &&
                    transientResponse >= ImmediateTriggerSupportResponse;
        }

        private long GetReportDueTimestamp(long reportTimestamp)
        {
            int latencyMilliseconds = Math.Max(
                _deviceLatencyProvider(),
                0);
            long latencyTicks = (long)Math.Round(
                latencyMilliseconds * Stopwatch.Frequency / 1000d,
                MidpointRounding.AwayFromZero);
            return reportTimestamp > long.MaxValue - latencyTicks
                ? long.MaxValue
                : reportTimestamp + latencyTicks;
        }

        private float GetBassResponse(
            NAudioComplex[] fftBuffer,
            int fftLength,
            ref BassFeatureState state)
        {
            double lowBassPower = GetAverageBandPower(
                LowBassBand, fftBuffer, fftLength);
            double bassNotePower = GetAverageBandPower(
                BassNoteBand, fftBuffer, fftLength);
            double coreBassPower = Math.Max(lowBassPower, bassNotePower * 0.9d);
            double upperBassPower = GetAverageBandPower(
                UpperBassBand, fftBuffer, fftLength);

            // Upper bass contributes only when lower-frequency energy is present.
            double supportedUpperBassPower = Math.Min(
                upperBassPower,
                coreBassPower * 1.35d);
            double bassPower = coreBassPower + supportedUpperBassPower * 0.2d;

            double lowMidPower = GetAverageBandPower(
                LowMidReferenceBand, fftBuffer, fftLength);
            double midPower = GetAverageBandPower(
                MidReferenceBand, fftBuffer, fftLength);
            // Compensate for bandwidth differences before testing dominance.
            double referencePower = Math.Max(
                lowMidPower * 2.3d,
                midPower * 1.6d);

            return GetBassResponseFromPowers(
                bassPower,
                referencePower,
                ref state);
        }

        private static float GetBassResponseFromPowers(
            double bassPower,
            double referencePower,
            ref BassFeatureState state)
        {
            float bassDecibels = PowerToDecibels(bassPower);
            float referenceDecibels = PowerToDecibels(referencePower);
            if (!state.Initialized)
            {
                // Leave headroom below the first report so a song beginning
                // on a kick is still recognized as a transient.
                state.SlowBassDecibels = MathF.Max(
                    SilenceFloorDecibels,
                    bassDecibels - BassRiseCeilingDecibels);
                state.SlowReferenceDecibels = referenceDecibels;
                state.PreviousBassDecibels = bassDecibels;
                state.Initialized = true;
            }

            float frameBassRise = MathF.Max(
                0f,
                bassDecibels - state.PreviousBassDecibels);
            state.PreviousBassDecibels = bassDecibels;

            float bassRise = MathF.Max(
                0f,
                bassDecibels - state.SlowBassDecibels);
            float referenceRise = MathF.Max(
                0f,
                referenceDecibels - state.SlowReferenceDecibels);

            float dominance = SmoothRange(
                bassDecibels - referenceDecibels,
                BassDominanceFloorDecibels,
                BassDominanceCeilingDecibels);
            float sharpAttackTarget = bassDecibels >=
                    SharpAttackLevelFloorDecibels
                ? SmoothRange(
                    frameBassRise,
                    SharpAttackRiseFloorDecibels,
                    SharpAttackRiseCeilingDecibels)
                : 0f;
            float sharpAttackDecay = MathF.Exp(
                -1f / (AnalysisReportsPerSecond * SharpAttackReleaseSeconds));
            state.SharpAttack = MathF.Max(
                sharpAttackTarget,
                state.SharpAttack * sharpAttackDecay);

            // Give sharp, bass-dominant attacks a short-lived boost.
            float harmonicBassConfidence = SmoothRange(
                dominance,
                HarmonicBassConfidenceFloor,
                HarmonicBassConfidenceCeiling) *
                state.SharpAttack * HarmonicBassAttackBoost;
            float bassConfidence = MathF.Max(
                dominance,
                harmonicBassConfidence);
            // Ease the reference penalty only for clearly bass-dominant attacks.
            float referenceRiseRejection = 0.7f - dominance * 0.35f;
            float bassOnlyRise = bassRise -
                referenceRise * referenceRiseRejection;

            const float reportSeconds = 1f / AnalysisReportsPerSecond;
            state.SlowBassDecibels = SmoothFeatureBaseline(
                state.SlowBassDecibels,
                bassDecibels,
                reportSeconds);
            state.SlowReferenceDecibels = SmoothFeatureBaseline(
                state.SlowReferenceDecibels,
                referenceDecibels,
                reportSeconds);

            float level = SmoothRange(
                bassDecibels,
                BassLevelFloorDecibels,
                BassLevelCeilingDecibels);
            float transient = SmoothRange(
                bassOnlyRise,
                BassRiseFloorDecibels,
                BassRiseCeilingDecibels);

            float response = level * bassConfidence *
                (SustainedBassResponse +
                    (1f - SustainedBassResponse) * transient);
            return Math.Clamp(response, 0f, 1f);
        }

        private float GetConfirmedResponse(
            float unprocessedResponse,
            bool allowImmediateTrigger)
        {
            _unprocessedReportHistory[_unprocessedReportWriteIndex] =
                unprocessedResponse;
            _unprocessedReportWriteIndex =
                (_unprocessedReportWriteIndex + 1) %
                _unprocessedReportHistory.Length;
            _availableUnprocessedReportCount = Math.Min(
                _availableUnprocessedReportCount + 1,
                _unprocessedReportHistory.Length);

            float confirmedResponse = allowImmediateTrigger
                ? unprocessedResponse
                : 0f;
            if (!allowImmediateTrigger &&
                _availableUnprocessedReportCount == 2)
            {
                // Accept the second agreeing report during startup.
                confirmedResponse = MathF.Min(
                    _unprocessedReportHistory[0],
                    _unprocessedReportHistory[1]);
            }
            else if (!allowImmediateTrigger &&
                _availableUnprocessedReportCount >= 3)
            {
                // Use a median as two-of-three confirmation.
                confirmedResponse = MedianOfThree(
                    _unprocessedReportHistory[0],
                    _unprocessedReportHistory[1],
                    _unprocessedReportHistory[2]);
            }

            return confirmedResponse;
        }

        private static float MedianOfThree(float first, float second, float third)
        {
            return first + second + third -
                MathF.Min(first, MathF.Min(second, third)) -
                MathF.Max(first, MathF.Max(second, third));
        }

        private static float SmoothFeatureBaseline(
            float current,
            float target,
            float seconds)
        {
            float timeConstant = target > current
                ? FeatureBaselineAttackSeconds
                : FeatureBaselineReleaseSeconds;
            float mix = 1f - MathF.Exp(-seconds / timeConstant);
            return MathF.Max(
                SilenceFloorDecibels,
                current + (target - current) * mix);
        }

        private double GetAverageBandPower(
            FrequencyBand band,
            NAudioComplex[] fftBuffer,
            int fftLength)
        {
            int firstBin = Math.Max(1, (int)MathF.Ceiling(
                band.Minimum * fftLength / _sampleRate));
            int lastBin = Math.Min(fftLength / 2 - 1, (int)MathF.Floor(
                band.Maximum * fftLength / _sampleRate));
            if (lastBin < firstBin)
            {
                return 0d;
            }

            double squaredMagnitude = 0d;
            for (int bin = firstBin; bin <= lastBin; bin++)
            {
                // NAudio's forward FFT is normalized by N. Four compensates
                // for the negative-frequency half and the Hann coherent gain.
                float real = fftBuffer[bin].X * 4f;
                float imaginary = fftBuffer[bin].Y * 4f;
                squaredMagnitude += real * real + imaginary * imaginary;
            }

            // Normalize by bin count before the Hann/RMS correction so wider
            // bands do not dominate solely because they contain more bins.
            int binCount = lastBin - firstBin + 1;
            return squaredMagnitude / (3d * binCount);
        }

        private static float PowerToDecibels(double averageBandPower)
        {
            return (float)(10d * Math.Log10(
                Math.Max(averageBandPower, 1e-12d)));
        }

        private static float SmoothRange(float value, float floor, float ceiling)
        {
            float normalized = Math.Clamp(
                (value - floor) / (ceiling - floor),
                0f,
                1f);
            return SmoothStep(normalized);
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }

        private void ResetAnalysis()
        {
            lock (_analysisGate)
            {
                Array.Clear(_sampleRing, 0, _sampleRing.Length);
                Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
                Array.Clear(_fastFftBuffer, 0, _fastFftBuffer.Length);
                _delayedReports.Clear();
                Array.Clear(
                    _unprocessedReportHistory,
                    0,
                    _unprocessedReportHistory.Length);
                _sampleWriteIndex = 0;
                _availableSampleCount = 0;
                _samplesSinceAnalysis = 0;
                _unprocessedReportWriteIndex = 0;
                _availableUnprocessedReportCount = 0;
                _featureState = default;
                _fastFeatureState = default;
                _transientFeatureState = default;
                _bassTransientDetector.Reset();
            }

            lock (_spectrumSampleGate)
            {
                Array.Clear(
                    _recentSpectrumSamples,
                    0,
                    _recentSpectrumSamples.Length);
                _recentSpectrumSampleWriteIndex = 0;
            }

            Volatile.Write(ref _reportedPowerX, 0f);
            Volatile.Write(ref _reportedPowerY, 0f);
            Volatile.Write(ref _reportedPowerZ, 0f);
            Volatile.Write(ref _reportedPowerW, 0f);
            Volatile.Write(ref _lastReportTimestamp, 0L);
            _power = Vector4.Zero;
            _unprocessed = Vector4.Zero;
            _targetPower = Vector4.Zero;
        }

        private static SampleEncoding GetSampleEncoding(WaveFormat format)
        {
            WaveFormatEncoding encoding = format.Encoding;
            if (format is WaveFormatExtensible extensible)
            {
                if (extensible.SubFormat == IeeeFloatSubFormat)
                {
                    encoding = WaveFormatEncoding.IeeeFloat;
                }
                else if (extensible.SubFormat == PcmSubFormat)
                {
                    encoding = WaveFormatEncoding.Pcm;
                }
            }

            if (encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                return SampleEncoding.Float32;
            }
            if (encoding != WaveFormatEncoding.Pcm)
            {
                return SampleEncoding.Unsupported;
            }

            return format.BitsPerSample switch
            {
                8 => SampleEncoding.Pcm8,
                16 => SampleEncoding.Pcm16,
                24 => SampleEncoding.Pcm24,
                32 => SampleEncoding.Pcm32,
                _ => SampleEncoding.Unsupported,
            };
        }

        private static float ReadSample(byte[] buffer, int offset, SampleEncoding encoding)
        {
            return encoding switch
            {
                SampleEncoding.Float32 => BitConverter.ToSingle(buffer, offset),
                SampleEncoding.Pcm8 => (buffer[offset] - 128) / 128f,
                SampleEncoding.Pcm16 => BitConverter.ToInt16(buffer, offset) / 32768f,
                SampleEncoding.Pcm24 => ReadPcm24(buffer, offset) / 8388608f,
                SampleEncoding.Pcm32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                _ => 0f,
            };
        }

        private static int ReadPcm24(byte[] buffer, int offset)
        {
            int value = buffer[offset] |
                buffer[offset + 1] << 8 |
                buffer[offset + 2] << 16;
            return (value & 0x800000) != 0 ? value | unchecked((int)0xff000000) : value;
        }

        private sealed class LowLatencyLoopbackCapture : WasapiCapture
        {
            private readonly MMDevice _endpoint;

            public LowLatencyLoopbackCapture()
                : this(WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice())
            {
            }

            private LowLatencyLoopbackCapture(MMDevice endpoint)
                : base(endpoint, false, CaptureBufferMilliseconds)
            {
                _endpoint = endpoint;
            }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags()
            {
                return AudioClientStreamFlags.Loopback |
                    base.GetAudioClientStreamFlags();
            }

            public new void Dispose()
            {
                try
                {
                    base.Dispose();
                }
                finally
                {
                    _endpoint.Dispose();
                }
            }
        }

        private sealed class BassTransientDetector
        {
            private const float LowCutoffHertz = 25f;
            private const float BassCutoffHertz = 190f;
            private const float ReferenceCutoffHertz = 760f;
            private const float ButterworthQ = 0.70710678f;
            private const float EnvelopeAttackSeconds = 0.006f;
            private const float EnvelopeReleaseSeconds = 0.045f;

            private BiquadLowPass _lowCutoff;
            private BiquadLowPass _bassCutoff;
            private BiquadLowPass _referenceCutoff;
            private int _sampleRate;
            private double _bassPower;
            private double _referencePower;

            public void PushSample(float sample, int sampleRate)
            {
                sampleRate = Math.Max(1, sampleRate);
                if (_sampleRate != sampleRate)
                {
                    Configure(sampleRate);
                }

                float input = Math.Clamp(sample, -4f, 4f);
                float low = _lowCutoff.Process(input);
                float bassTop = _bassCutoff.Process(input);
                float referenceTop = _referenceCutoff.Process(input);
                float bass = bassTop - low;
                float reference = referenceTop - bassTop;

                UpdateEnvelope(
                    ref _bassPower,
                    bass * bass,
                    sampleRate);
                UpdateEnvelope(
                    ref _referencePower,
                    reference * reference,
                    sampleRate);
            }

            public void GetBandPowers(
                out double bassPower,
                out double referencePower)
            {
                bassPower = Math.Max(_bassPower, 1e-12d);
                referencePower = Math.Max(_referencePower, 1e-12d);
            }

            public void Reset()
            {
                _lowCutoff.ResetState();
                _bassCutoff.ResetState();
                _referenceCutoff.ResetState();
                _bassPower = 0d;
                _referencePower = 0d;
            }

            private void Configure(int sampleRate)
            {
                _sampleRate = sampleRate;
                _lowCutoff.Configure(
                    LowCutoffHertz,
                    sampleRate,
                    ButterworthQ);
                _bassCutoff.Configure(
                    BassCutoffHertz,
                    sampleRate,
                    ButterworthQ);
                _referenceCutoff.Configure(
                    ReferenceCutoffHertz,
                    sampleRate,
                    ButterworthQ);
                _bassPower = 0d;
                _referencePower = 0d;
            }

            private static void UpdateEnvelope(
                ref double current,
                double target,
                int sampleRate)
            {
                float seconds = target > current
                    ? EnvelopeAttackSeconds
                    : EnvelopeReleaseSeconds;
                double retain = Math.Exp(-1d / (sampleRate * seconds));
                current = current * retain + target * (1d - retain);
            }
        }

        private struct BiquadLowPass
        {
            private float _b0;
            private float _b1;
            private float _b2;
            private float _a1;
            private float _a2;
            private float _z1;
            private float _z2;

            public void Configure(float cutoff, int sampleRate, float q)
            {
                float maximumCutoff = sampleRate * 0.49f;
                float omega = 2f * MathF.PI *
                    Math.Clamp(cutoff, 1f, maximumCutoff) / sampleRate;
                float sine = MathF.Sin(omega);
                float cosine = MathF.Cos(omega);
                float alpha = sine / (2f * q);
                float inverseA0 = 1f / (1f + alpha);

                _b0 = (1f - cosine) * 0.5f * inverseA0;
                _b1 = (1f - cosine) * inverseA0;
                _b2 = _b0;
                _a1 = -2f * cosine * inverseA0;
                _a2 = (1f - alpha) * inverseA0;
                ResetState();
            }

            public float Process(float sample)
            {
                float output = _b0 * sample + _z1;
                _z1 = _b1 * sample - _a1 * output + _z2;
                _z2 = _b2 * sample - _a2 * output;
                return float.IsFinite(output) ? output : 0f;
            }

            public void ResetState()
            {
                _z1 = 0f;
                _z2 = 0f;
            }
        }

        private struct BassFeatureState
        {
            public float SlowBassDecibels;
            public float SlowReferenceDecibels;
            public float PreviousBassDecibels;
            public float SharpAttack;
            public bool Initialized;
        }

        private readonly record struct FrequencyBand(float Minimum, float Maximum);

        private enum SampleEncoding
        {
            Unsupported,
            Float32,
            Pcm8,
            Pcm16,
            Pcm24,
            Pcm32,
        }
    }
}
