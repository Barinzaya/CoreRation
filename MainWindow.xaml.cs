using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CoreRation
{
    public partial class MainWindow : Window
    {
        private AppConfig appConfig { get; set; }

        private readonly OpenFileDialog loadDialog;
        private readonly SaveFileDialog saveDialog;

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
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs ev)
        {
            var profile = CurrentProfile;
            if(profile == null) return;

            var numCores = Environment.ProcessorCount;
            var allMask = (IntPtr)(1L << numCores) - 1;
            long otherMask;

            if(string.IsNullOrWhiteSpace(profile.OtherCores))
            {
                otherMask = 0;
            }
            else
            {
                otherMask = ParseMask(profile.OtherCores, numCores);
            }

            var dict = new Dictionary<string, ProcessConfig>(StringComparer.OrdinalIgnoreCase);
            foreach(var process in profile.Processes)
            {
                if(dict.ContainsKey(process.Name))
                {
                    MessageBox.Show($"Profile \"{profile.Name}\" has multiple processes named \"{process.Name}\".", "Duplicate Process", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if(string.IsNullOrWhiteSpace(process.Cores))
                {
                    process.coreMask = 0;
                }
                else
                {
                    process.coreMask = ParseMask(process.Cores, numCores);
                }

                dict.Add(process.Name, process);
            }

            foreach(var process in Process.GetProcesses())
            {
                if(dict.TryGetValue(process.ProcessName, out var config))
                {
                    var priorityClass = config.Priority.ToPriorityClass();
                    if(priorityClass.HasValue)
                    {
                        try
                        {
                            process.PriorityClass = priorityClass.Value;
                        }
                        catch {}
                    }

                    if(config.coreMask != 0)
                    {
                        try
                        {
                            process.ProcessorAffinity = (IntPtr)config.coreMask;
                        }
                        catch {}
                    }
                }
                else if(otherMask != 0)
                {
                    try
                    {
                        process.ProcessorAffinity = (IntPtr)otherMask;
                    }
                    catch {}
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
            }
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

        private void SaveButton_Click(object sender, RoutedEventArgs ev)
        {
            var result = saveDialog.ShowDialog(this);
            if(result == true)
            {
                var path = saveDialog.FileName;

                try
                {
                    SaveConfig(path, appConfig);
                }
                catch(Exception e)
                {
                    MessageBox.Show($"Failed to save configuration to <{path}>: {e.Message}", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
                return JsonConvert.DeserializeObject<AppConfig>(s);
            }
        }

        private static readonly Regex RANGE_REGEX = new Regex(@"^\s*(\d+)(?:\s*-\s*(\d+))?\s*$");

        private long ParseMask(string def, int numCores)
        {
            var result = 0L;

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
                    (a, b) = (b, a);
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
            }
        }
    }
}
