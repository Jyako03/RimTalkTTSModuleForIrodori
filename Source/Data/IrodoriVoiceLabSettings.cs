using Verse;

namespace RimTalk.TTS.Data
{
    /// <summary>
    /// Generation controls used only by Irodori Voice Lab workflows.
    /// These values are intentionally independent from normal gameplay TTS settings so users can
    /// search voices quickly without changing the quality/performance profile used during play.
    /// </summary>
    public sealed class IrodoriVoiceLabSettings : IExposable
    {
        public const int DefaultNumSteps = 24;
        public const float DefaultSpeed = 1.0f;
        public const float DefaultDurationScale = 1.0f;
        public const float DefaultSwayCoeff = -1.0f;
        public const float DefaultCfgScaleText = 3.0f;
        public const float DefaultCfgScaleSpeaker = 5.0f;
        public const float DefaultCfgScaleCaption = 0.0f;
        public const float DefaultMaxRefSeconds = 120.0f;
        public const string DefaultTScheduleMode = "linear";
        public const string DefaultCfgGuidanceMode = "independent";

        public int NumSteps = DefaultNumSteps;
        public float Speed = DefaultSpeed;
        public float DurationScale = DefaultDurationScale;
        public float SwayCoeff = DefaultSwayCoeff;
        public float CfgScaleText = DefaultCfgScaleText;
        public float CfgScaleSpeaker = DefaultCfgScaleSpeaker;
        public float CfgScaleCaption = DefaultCfgScaleCaption;
        public float MaxRefSeconds = DefaultMaxRefSeconds;
        public string TScheduleMode = DefaultTScheduleMode;
        public string CfgGuidanceMode = DefaultCfgGuidanceMode;

        public void ExposeData()
        {
            Scribe_Values.Look(ref NumSteps, "numSteps", DefaultNumSteps);
            Scribe_Values.Look(ref Speed, "speed", DefaultSpeed);
            Scribe_Values.Look(ref DurationScale, "durationScale", DefaultDurationScale);
            Scribe_Values.Look(ref SwayCoeff, "swayCoeff", DefaultSwayCoeff);
            Scribe_Values.Look(ref CfgScaleText, "cfgScaleText", DefaultCfgScaleText);
            Scribe_Values.Look(ref CfgScaleSpeaker, "cfgScaleSpeaker", DefaultCfgScaleSpeaker);
            Scribe_Values.Look(ref CfgScaleCaption, "cfgScaleCaption", DefaultCfgScaleCaption);
            Scribe_Values.Look(ref MaxRefSeconds, "maxRefSeconds", DefaultMaxRefSeconds);
            Scribe_Values.Look(ref TScheduleMode, "tScheduleMode", DefaultTScheduleMode);
            Scribe_Values.Look(ref CfgGuidanceMode, "cfgGuidanceMode", DefaultCfgGuidanceMode);

            Normalize();
        }

        public void ResetDefaults()
        {
            NumSteps = DefaultNumSteps;
            Speed = DefaultSpeed;
            DurationScale = DefaultDurationScale;
            SwayCoeff = DefaultSwayCoeff;
            CfgScaleText = DefaultCfgScaleText;
            CfgScaleSpeaker = DefaultCfgScaleSpeaker;
            CfgScaleCaption = DefaultCfgScaleCaption;
            MaxRefSeconds = DefaultMaxRefSeconds;
            TScheduleMode = DefaultTScheduleMode;
            CfgGuidanceMode = DefaultCfgGuidanceMode;
        }

        public void Normalize()
        {
            if (NumSteps < 1) NumSteps = DefaultNumSteps;
            if (Speed <= 0f) Speed = DefaultSpeed;
            if (DurationScale <= 0f) DurationScale = DefaultDurationScale;
            if (CfgScaleText < 0f) CfgScaleText = DefaultCfgScaleText;
            if (CfgScaleSpeaker < 0f) CfgScaleSpeaker = DefaultCfgScaleSpeaker;
            if (CfgScaleCaption < 0f) CfgScaleCaption = DefaultCfgScaleCaption;
            if (MaxRefSeconds <= 0f) MaxRefSeconds = DefaultMaxRefSeconds;
            if (string.IsNullOrWhiteSpace(TScheduleMode)) TScheduleMode = DefaultTScheduleMode;
            if (string.IsNullOrWhiteSpace(CfgGuidanceMode)) CfgGuidanceMode = DefaultCfgGuidanceMode;
        }
    }
}
