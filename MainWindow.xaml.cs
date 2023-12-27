using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.VisualBasic;
using FluentFTP;
using System.Collections.ObjectModel;
using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Windows.Threading;

namespace popcornftp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public string ConfigFile = @"./config.json";

        public MainWindow()
        {
            InitializeComponent();

            btnListFiles.Click += new RoutedEventHandler(btnListFiles_Click);

            if(System.Diagnostics.Debugger.IsAttached)
            {
                ConfigFile = @"c:\temp\config.json";
            }

            lblStatus.Content = "Initializing";

            if (File.Exists(ConfigFile))
            {
                var json = File.ReadAllText(ConfigFile);

                var options = JsonConvert.DeserializeObject<ConfigOptions>(json);
                txtSaveFile.Text = options.FileName;
                txtSaveLocation.Text = options.FilePath;
                txtFtpPassword.Password = options.FtpPassword;
                txtFtpServer.Text = options.FtpSite;
                txtFtpUser.Text = options.FtpUser;
            }

            btnListFiles_Click(this, new RoutedEventArgs());

            lblStatus.Content = "";
        }

        private void options_changed(object sender, RoutedEventArgs e)
        {
            lblStatus.Content = "Saving config";
            var options = new ConfigOptions()
            {
                FileName = txtSaveFile.Text,
                FilePath = txtSaveLocation.Text,
                FtpSite = txtFtpServer.Text,
                FtpUser = txtFtpUser.Text,
                FtpPassword = txtFtpPassword.Password,
            };

            var json = JsonConvert.SerializeObject(options, Formatting.Indented);
            File.WriteAllText(ConfigFile, json);
            lblStatus.Content = "";
        }

        private void btnSaveFile_Click(object sender, RoutedEventArgs e)
        {
            lblStatus.Content = "Downloading File";

            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.FileName = Path.Combine(txtSaveLocation.Text, txtSaveFile.Text);
            dlg.DefaultExt = ".cok";
            dlg.Filter = "Cities Skylines 2 Saves (.cok)|*.cok";

            bool? result = dlg.ShowDialog();
            if(result == true) { 
                txtSaveFile.Text = Path.GetFileName(dlg.FileName);
                txtSaveLocation.Text = Path.GetDirectoryName(dlg.FileName);
            }

            lblStatus.Content = "Download Complete";
        }


        private void btnSaveLocation_Click(object sender, RoutedEventArgs e)
        {

        }


        private void btnListFiles_Click(object sender, RoutedEventArgs e){
            try
            {
                var client = new FtpClient(txtFtpServer.Text, txtFtpUser.Text, txtFtpPassword.Password);
                client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                client.Config.ValidateAnyCertificate = true;

                var items = new ObservableCollection<string>();

                client.AutoConnect();

                var files = new List<string>();

                foreach (var ftpFile in client.GetListing("/files"))
                {
                    if (ftpFile.Type == FtpObjectType.File && ftpFile.FullName.EndsWith("cok"))
                    {
                        files.Add(ftpFile.Name);
                    }
                }

                lbxFiles.ItemsSource = files;
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void btnUpload_Click(object sender, RoutedEventArgs e)
        {
            try
            {


                btnUpload.IsEnabled = false;

                var token = new CancellationToken();

                var client = new AsyncFtpClient(txtFtpServer.Text, txtFtpUser.Text, txtFtpPassword.Password);
                client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                client.Config.ValidateAnyCertificate = true;

                var fullPath = System.IO.Path.Combine(txtSaveLocation.Text, txtSaveFile.Text);

                string sfx = "";

                using (FileStream fs = new FileStream(fullPath, FileMode.Open))
                {
                    using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
                    {
                        var metadataEntry = archive.Entries.FirstOrDefault(entry => entry.Name.EndsWith("SaveGameMetadata"));
                        if (metadataEntry != null)
                        {
                            using (var metadataStream = metadataEntry.Open())
                            {
                                var stringReader = new StreamReader(metadataStream);
                                var metadataJson = stringReader.ReadToEnd();
                                dynamic metadata = JObject.Parse(metadataJson);
                                sfx = "." + metadata.simulationDate.year + "." + metadata.simulationDate.month + "." + metadata.simulationDate.hour + "." + metadata.simulationDate.minute;
                            }
                        }
                    }
                }

                var destFileName = txtSaveFile.Text;
                destFileName = destFileName.Replace(".cok", sfx + ".cok");

                await client.AutoConnect(token);

                Progress<FtpProgress> progress = new Progress<FtpProgress>((FtpProgress p) =>
                {
                    if (p.Progress == 1)
                    {

                    }
                    else
                    {
                        progressBar.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Normal
                            , new DispatcherOperationCallback(delegate
                            {
                                progressBar.Value = p.Progress;
                                //do what you need to do on UI Thread
                                return null;
                            }), null);
                    }
                });

                lblStatus.Content = "Uploading File";
                await client.UploadFile(fullPath, @"/files/" + destFileName, FtpRemoteExists.Overwrite, true, FtpVerify.None, progress, token);
                lblStatus.Content = "Uploading cid file";
                await client.UploadFile(fullPath + ".cid", @"/files/" + destFileName + ".cid", FtpRemoteExists.Overwrite, true, FtpVerify.None, progress, token);
                lblStatus.Content = "Upload Complete";
                btnUpload.IsEnabled = true;

            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                var token = new CancellationToken();

                (sender as Button).IsEnabled = false;

                var client = new AsyncFtpClient(txtFtpServer.Text, txtFtpUser.Text, txtFtpPassword.Password);
                client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                client.Config.ValidateAnyCertificate = true;

                var fullPath = System.IO.Path.Combine(txtSaveLocation.Text, txtSaveFile.Text);

                var remoteFile = (sender as Button).DataContext.ToString();

                if (string.IsNullOrEmpty(fullPath))
                {
                    fullPath = @"C:\temp\" + (sender as Button).DataContext.ToString();
                }

                Progress<FtpProgress> progress = new Progress<FtpProgress>((FtpProgress p) =>
                {
                    if (p.Progress == 1)
                    {

                    }
                    else
                    {
                        progressBar.Dispatcher.BeginInvoke(
                            System.Windows.Threading.DispatcherPriority.Normal
                            , new DispatcherOperationCallback(delegate
                            {
                                progressBar.Value = p.Progress;
                                //do what you need to do on UI Thread
                                return null;
                            }), null);
                    }
                });

                await client.AutoConnect(token);
                lblStatus.Content = "Downloading File";
                await client.DownloadFile(fullPath, @"/files/" + remoteFile, FtpLocalExists.Overwrite, FtpVerify.None, progress, token);
                lblStatus.Content = "Downloading cid File";
                await client.DownloadFile(fullPath + ".cid", @"/files/" + remoteFile + ".cid", FtpLocalExists.Overwrite, FtpVerify.None, progress, token);

                (sender as Button).IsEnabled = true;
                lblStatus.Content = "Download Complete";
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.Message);    
            }
        }

    }
}
