using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CoreRation
{
    public class AppConfig : BaseConfig
    {
        public ObservableCollection<ProfileConfig> Profiles
        {
            get => profiles;
            set => UpdateProperty(ref profiles, value);
        }

        private ObservableCollection<ProfileConfig> profiles;
    }

    public class ProfileConfig : BaseConfig
    {
        public string Name
        {
            get => name;
            set => UpdateProperty(ref name, value);
        }

        [DefaultValue(500)]
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.IgnoreAndPopulate)]
        public int MonitorInterval
        {
            get => monitorInterval;
            set
            {
                if(value < 1)
                    throw new ArgumentOutOfRangeException("value", "MonitorInterval must be a positive value.");

                UpdateProperty(ref monitorInterval, value);
            }
        }

        [JsonProperty(DefaultValueHandling=DefaultValueHandling.IgnoreAndPopulate)]
        public string OtherCores
        {
            get => otherCores;
            set => UpdateProperty(ref otherCores, value);
        }

        [JsonProperty(DefaultValueHandling=DefaultValueHandling.IgnoreAndPopulate)]
        public ObservableCollection<ProcessConfig> Processes
        {
            get => processes;
            set => UpdateProperty(ref processes, value);
        }

        private int monitorInterval = 500;
        private string name;
        private string otherCores;
        private ObservableCollection<ProcessConfig> processes;

        [JsonIgnore]
        public long otherMask { get; set; }
    }

    public class ProcessConfig : BaseConfig
    {
        public string Name
        {
            get => name;
            set => UpdateProperty(ref name, value);
        }

        [JsonProperty(DefaultValueHandling=DefaultValueHandling.IgnoreAndPopulate)]
        public string Cores
        {
            get => cores;
            set => UpdateProperty(ref cores, value);
        }

        [DefaultValue(ProcessPriority.NoChange)]
        [JsonConverter(typeof(StringEnumConverter))]
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.IgnoreAndPopulate)]
        public ProcessPriority Priority
        {
            get => priority;
            set => UpdateProperty(ref priority, value);
        }

        private string name;
        private string cores;
        private ProcessPriority priority;
    }

    public abstract class BaseConfig : INotifyPropertyChanged, INotifyPropertyChanging
    {
        protected void UpdateProperty<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if(!EqualityComparer<T>.Default.Equals(field, value))
            {
                PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(name));
                field = value;
                PropertyChanged?.Invoke(this,  new PropertyChangedEventArgs(name));
            }
        }

        public event PropertyChangingEventHandler PropertyChanging;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public enum ProcessPriority
    {
        NoChange,

        Low,
        BelowNormal,
        Normal,
        AboveNormal,
        High,
        Realtime,
    }

    public static class ConfigUtil
    {
        private static readonly Regex RANGE_REGEX = new Regex(@"^\s*(\d+)(?:\s*-\s*(\d+))?\s*$");

        public static long ParseMask(string def, int numCores)
        {
            var result = 0L;

            if(string.IsNullOrWhiteSpace(def))
            {
                return 0L;
            }

            var start = 0;
            while(start < def.Length)
            {
                var end = def.IndexOf(',', start);
                if(end < 0) end = def.Length;

                var match = RANGE_REGEX.Match(def, start, end - start);
                if(!match.Success)
                {
                    var part = def.Substring(start, end - start);
                    throw new Exception($"Failed to parse core specification: \"{part}\" is not a valid core number or range.");
                }

                var a = int.Parse(match.Groups[1].Value);
                var b = a;

                if(match.Groups[2].Success)
                {
                    b = int.Parse(match.Groups[2].Value);
                }

                if(a > b)
                {
                    var c = a;
                    a = b;
                    b = c;
                }

                if(b >= numCores)
                {
                    var part = def.Substring(start, end - start);
                    throw new Exception($"Failed to parse core specification: {b} is out of range in core specification \"{part}\".");
                }

                for(var x = a; x <= b; x++)
                {
                    result |= (1L << x);
                }

                start = end + 1;
            }

            return result;
        }
    }

    public static class ProcessPriorityExt
    {
        public static ProcessPriorityClass? ToPriorityClass(this ProcessPriority priority)
        {
            switch(priority)
            {
                case ProcessPriority.NoChange: return null;

                case ProcessPriority.Low: return ProcessPriorityClass.Idle;
                case ProcessPriority.BelowNormal: return ProcessPriorityClass.BelowNormal;
                case ProcessPriority.Normal: return ProcessPriorityClass.Normal;
                case ProcessPriority.AboveNormal: return ProcessPriorityClass.AboveNormal;
                case ProcessPriority.High: return ProcessPriorityClass.High;
                case ProcessPriority.Realtime: return ProcessPriorityClass.RealTime;

                default: throw new InvalidEnumArgumentException(nameof(priority), (int)priority, typeof(ProcessPriority));
            }
        }
    }
}
