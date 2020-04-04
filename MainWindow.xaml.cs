using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace CoreRation
{
    public partial class MainWindow : Window
    {
        private AppConfig appConfig { get; set; }

        private readonly OpenFileDialog loadDialog;
        private readonly SaveFileDialog saveDialog;

        private bool changed = false;
        private string currentConfigPath = null;

        private DispatcherTimer monitorTimer;
        private CompiledProfile monitorProfile = null;
        private ISet<int> newMonitorPIDs, oldMonitorPIDs;

        private ProfileConfig CurrentProfile
        {
            get => (ProfileConfig)ProfileList.SelectedItem;
            set => ProfileList.SelectedItem = value;
        }

        private ProcessConfig CurrentProcess
        {
            get => (ProcessConfig)ProcessList.SelectedItem;
            set => ProcessList.SelectedItem = value;
        }

        public MainWindow()
        {
            InitializeComponent();

            using(var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                if(!principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show("This application was not run as Administrator. Some functionality may not work.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            loadDialog = new OpenFileDialog
            {
                AddExtension = true,
                CheckFileExists = true,
                FileName = "default.crs",
                Filter = "CoreRation Settings|*.crs",
            };

            saveDialog = new SaveFileDialog
            {
                AddExtension = true,
                FileName = "default.crs",
                Filter = "CoreRation Settings|*.crs",
            };

            var appPath = Assembly.GetExecutingAssembly().Location;
            var appDir = Path.GetDirectoryName(appPath);
            var appConfigPath = Path.Combine(appDir, "default.crs");

            var userDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userConfigDir = Path.Combine(userDir, "CoreRation");
            var userConfigPath = Path.Combine(userConfigDir, "default.crs");

            try
            {
                appConfig = LoadConfig(appConfigPath);
            }
            catch(FileNotFoundException) {}
            catch(Exception e)
            {
                MessageBox.Show($"Failed to load configuration from <{appConfigPath}>: {e.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if(appConfig == null)
            {
                Directory.CreateDirectory(userConfigDir);

                try
                {
                    appConfig = LoadConfig(userConfigPath);
                }
                catch(FileNotFoundException) {}
                catch(Exception e)
                {
                    MessageBox.Show($"Failed to load configuration from <{userConfigPath}>: {e.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            appConfig = appConfig ?? new AppConfig();

            DataContext = appConfig;
            ProcessPanel.DataContext = null;
            ProfilePanel.DataContext = appConfig?.Profiles?.FirstOrDefault();

            var profilesView = (CollectionViewSource)FindResource("Profiles");
            profilesView.IsLiveSortingRequested = true;
            profilesView.LiveSortingProperties.Add("Name");

            var processesView = (CollectionViewSource)ProfilePanel.FindResource("Processes");
            processesView.IsLiveSortingRequested = true;
            processesView.LiveSortingProperties.Add("Name");

            ProcessList_SelectionChanged(this, null);
            ProfileList_SelectionChanged(this, null);

            ProcessPriorityField.ItemsSource = Enum.GetValues(typeof(ProcessPriority));

            monitorTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
            monitorTimer.Tick += MonitorTimer_Tick;

            newMonitorPIDs = new HashSet<int>();
            oldMonitorPIDs = new HashSet<int>();
        }

        private void AddProcessButton_Click(object sender, RoutedEventArgs ev)
        {
            var profile = CurrentProfile;
            if(profile == null) return;

            var process = new ProcessConfig
            {
                Name = "process",
            };

            profile.Processes = profile.Processes ?? new ObservableCollection<ProcessConfig>();
            profile.Processes.Add(process);
            CurrentProcess = process;

            changed = true;
        }

        private void AddProfileButton_Click(object sender, RoutedEventArgs ev)
        {
            var profile = new ProfileConfig
            {
                Name = "New Profile",
                Processes = new ObservableCollection<ProcessConfig>(),
            };

            appConfig.Profiles = appConfig.Profiles ?? new ObservableCollection<ProfileConfig>();
            appConfig.Profiles.Add(profile);
            CurrentProfile = profile;

            changed = true;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs ev)
        {
            if(CurrentProfile != null)
            {
                CompiledProfile compiled;

                try
                {
                    compiled = CompileProfile(CurrentProfile);
                }
                catch(Exception e)
                {
                    MessageBox.Show(e.Message, "Profile Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    foreach(var process in Process.GetProcesses())
                    {
                        compiled.Apply(process);
                    }
                }
                catch(Exception e)
                {
                    MessageBox.Show(e.Message, "Failed to Apply Profile", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
        }

        private void DelProcessButton_Click(object sender, RoutedEventArgs ev)
        {
            var process = CurrentProcess;
            var profile = CurrentProfile;
            if(process == null || profile == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete the process \"{process.Name}\"?", "Delete Process", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if(result == MessageBoxResult.Yes)
            {
                profile.Processes.Remove(process);
                changed = true;
            }
        }

        private void DelProfileButton_Click(object sender, RoutedEventArgs ev)
        {
            var profile = CurrentProfile;
            if(profile == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete the profile \"{profile.Name}\"?", "Delete Process", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if(result == MessageBoxResult.Yes)
            {
                appConfig.Profiles.Remove(profile);
                changed = true;
            }
        }

        private void Input_Changed(object sender, RoutedEventArgs ev)
        {
            changed = true;
        }

        private void LoadButton_Click(object sender, RoutedEventArgs ev)
        {
            var result = loadDialog.ShowDialog(this);
            if(result == true)
            {
                var path = loadDialog.FileName;

                try
                {
                    appConfig = LoadConfig(path);

                    DataContext = appConfig;
                    CurrentProfile = appConfig.Profiles?.FirstOrDefault();
                }
                catch(Exception e)
                {
                    MessageBox.Show($"Failed to load configuration from <{path}>: {e.Message}", "Load Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void MonitorButton_Click(object sender, RoutedEventArgs ev)
        {
            var monitoring = !monitorTimer.IsEnabled;
            if(monitoring)
            {
                CompiledProfile compiled;

                try
                {
                    compiled = CompileProfile(CurrentProfile);

                    if(CurrentProfile.MonitorInterval < 1)
                    {
                        throw new Exception("Monitor interval must be a positive number.");
                    }
                }
                catch(Exception e)
                {
                    MessageBox.Show(e.Message, "Profile Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                newMonitorPIDs.Clear();
                oldMonitorPIDs.Clear();

                monitorProfile = compiled;
                monitorTimer.Interval = new TimeSpan(0, 0, 0, 0, CurrentProfile.MonitorInterval);
                monitorTimer.Start();

                MonitorTimer_Tick(monitorTimer, EventArgs.Empty);
            }
            else
            {
                monitorTimer.Stop();
            }

            ApplyButton.IsEnabled = !monitoring;
            ResetButton.IsEnabled = !monitoring;
            MonitorButton.Content = monitoring ? "Stop Monitoring" : "Start Monitoring";

            ProfileList.IsEnabled = !monitoring;
            AddProfileButton.IsEnabled = !monitoring;
            DelProfileButton.IsEnabled = !monitoring;

            LoadButton.IsEnabled = !monitoring;
            SaveButton.IsEnabled = !monitoring;

            ProfileNameField.IsEnabled = !monitoring;
            OtherCoresField.IsEnabled = !monitoring;
            MonitorIntervalField.IsEnabled = !monitoring;

            AddProcessButton.IsEnabled = !monitoring;
            DelProcessButton.IsEnabled = !monitoring;

            ProcessNameField.IsEnabled = !monitoring;
            ProcessPriorityField.IsEnabled = !monitoring;
            ProcessCoresField.IsEnabled = !monitoring;
        }

        private void MonitorTimer_Tick(object sender, EventArgs ev)
        {
            newMonitorPIDs.Clear();

            foreach(var process in Process.GetProcesses())
            {
                var pid = process.Id;
                newMonitorPIDs.Add(pid);

                if(!oldMonitorPIDs.Contains(pid))
                {
                    monitorProfile.Apply(process);
                }
            }

            var temp = oldMonitorPIDs;
            oldMonitorPIDs = newMonitorPIDs;
            newMonitorPIDs = temp;
        }

        private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs ev)
        {
            var process = CurrentProcess;
            DelProcessButton.IsEnabled = (process != null);
            ProcessPanel.IsEnabled = (process != null);
            ProcessPanel.DataContext = process;
        }

        private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs ev)
        {
            var profile = CurrentProfile;
            DelProfileButton.IsEnabled = (profile != null);
            ProfilePanel.IsEnabled = (profile != null);
            ProfilePanel.DataContext = profile;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs ev)
        {
            RevertChanges();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs ev)
        {
            try
            {
                SaveConfigAs(appConfig);
            }
            catch(Exception e)
            {
                MessageBox.Show($"Failed to save configuration: {e.Message}", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Closed(object sender, EventArgs ev)
        {
            RevertChanges();
        }

        private void Window_Closing(object sender, CancelEventArgs ev)
        {
            if(!changed)
            {
                return;
            }

            var result = MessageBox.Show(
                "There are unsaved changes to the current profile. Would you like to save before exiting?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if(result == MessageBoxResult.Cancel)
            {
                ev.Cancel = true;
                return;
            }

            if(result == MessageBoxResult.Yes)
            {
                bool saved;
                try
                {
                    if(currentConfigPath == null)
                    {
                        saved = SaveConfigAs(appConfig);
                    }
                    else
                    {
                        SaveConfig(currentConfigPath, appConfig);
                        saved = true;
                    }
                }
                catch(Exception e)
                {
                    MessageBox.Show($"Failed to save configuration: {e.Message}", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    saved = false;
                }

                if(!saved)
                {
                    ev.Cancel = true;
                    return;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            changed = false;
        }

        public CompiledProfile CompileProfile(ProfileConfig profile)
        {
            var numCores = Environment.ProcessorCount;
            long otherMask = ConfigUtil.ParseMask(profile.OtherCores, numCores);

            var processes = new Dictionary<string, CompiledProcess>(StringComparer.OrdinalIgnoreCase);
            foreach(var process in profile.Processes)
            {
                if(processes.ContainsKey(process.Name))
                {
                    throw new Exception($"Profile \"{profile.Name}\" has multiple processes named \"{process.Name}\".");
                }

                processes.Add(process.Name, new CompiledProcess
                {
                    ProcessCores = ConfigUtil.ParseMask(process.Cores, numCores),
                    ProcessPriority = process.Priority.ToPriorityClass(),
                });
            }

            return new CompiledProfile
            {
                OtherCores = otherMask,
                Processes = processes,
            };
        }

        public AppConfig LoadConfig(string path)
        {
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);

            loadDialog.InitialDirectory = dir;
            loadDialog.FileName = name;

            saveDialog.InitialDirectory = dir;
            saveDialog.FileName = name;
            
            using(var file = new FileStream(path, FileMode.Open))
            using(var reader = new StreamReader(file, Encoding.UTF8))
            {
                var s = reader.ReadToEnd();
                var config =  JsonConvert.DeserializeObject<AppConfig>(s);

                changed = false;
                currentConfigPath = path;

                return config;
            }
        }

        public void RevertChanges()
        {
            var numCores = Environment.ProcessorCount;

            var allMask = (IntPtr)(1L << numCores) - 1;
            foreach(var process in Process.GetProcesses())
            {
                try
                {
                    process.ProcessorAffinity = allMask;
                }
                catch {}
            }
        }

        public void SaveConfig(string path, AppConfig appConfig)
        {
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);

            loadDialog.InitialDirectory = dir;
            loadDialog.FileName = name;

            saveDialog.InitialDirectory = dir;
            saveDialog.FileName = name;

            Directory.CreateDirectory(dir);

            using(var file = new FileStream(path, FileMode.Create))
            using(var writer = new StreamWriter(file, Encoding.UTF8))
            {
                var s = JsonConvert.SerializeObject(appConfig, Formatting.Indented);
                writer.Write(s);

                changed = false;
                currentConfigPath = path;
            }
        }

        public bool SaveConfigAs(AppConfig appConfig)
        {
            var result = saveDialog.ShowDialog(this);
            if(result == true)
            {
                var path = saveDialog.FileName;
                SaveConfig(path, appConfig);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public class CompiledProfile
    {
        public long OtherCores { get; set; }
        public IDictionary<string, CompiledProcess> Processes { get; set; }

        public void Apply(Process process)
        {
            long cores;
            ProcessPriorityClass? priority = null;

            if(Processes.TryGetValue(process.ProcessName, out var config))
            {
                cores = config.ProcessCores;
                priority = config.ProcessPriority;
            }
            else
            {
                cores = OtherCores;
            }

            if(priority.HasValue)
            {
                try
                {
                    process.PriorityClass = priority.Value;
                }
                catch {}
            }

            if(cores != 0)
            {
                try
                {
                    process.ProcessorAffinity = (IntPtr)cores;
                }
                catch {}
            }
        }
    }

    public class CompiledProcess
    {
        public long ProcessCores { get; set; }
        public ProcessPriorityClass? ProcessPriority { get; set; }
    }
}
