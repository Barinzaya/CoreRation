using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

        private string name;
        private string otherCores;
        private ObservableCollection<ProcessConfig> processes;
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

        [JsonIgnore]
        public long coreMask { get; set; }
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
