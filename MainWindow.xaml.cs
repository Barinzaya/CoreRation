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
using System.Text.RegularExpressions;
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
        private string currentConfig = null;

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
                ApplyProfile(CurrentProfile);
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

        private void Input_Changed(object sender, RoutedEventArgs e)
        {
            changed = true;
            Console.WriteLine(sender);
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

        private void Window_Closed(object sender, EventArgs e)
        {
            RevertChanges();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
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
                e.Cancel = true;
                return;
            }

            if(result == MessageBoxResult.Yes)
            {
                bool saved;
                try
                {
                    if(currentConfig == null)
                    {
                        saved = SaveConfigAs(appConfig);
                    }
                    else
                    {
                        SaveConfig(currentConfig, appConfig);
                        saved = true;
                    }
                }
                catch(Exception f)
                {
                    MessageBox.Show($"Failed to save configuration: {f.Message}", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    saved = false;
                }

                if(!saved)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            changed = false;
        }

        public void ApplyProfile(ProfileConfig profile)
        {
            var numCores = Environment.ProcessorCount;
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
                currentConfig = path;

                return config;
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
                currentConfig = path;
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
}
