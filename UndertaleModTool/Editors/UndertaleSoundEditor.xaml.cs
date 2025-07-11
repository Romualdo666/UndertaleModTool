﻿#pragma warning disable CA1416 // Validate platform compatibility

using NAudio.Vorbis;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
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
using UndertaleModLib;
using UndertaleModLib.Models;
using WpfAnimatedGif;

namespace UndertaleModTool
{
    /// <summary>
    /// Logika interakcji dla klasy UndertaleSoundEditor.xaml
    /// </summary>
    public partial class UndertaleSoundEditor : DataUserControl
    {
        private WaveOutEvent waveOut;
        private WaveFileReader wavReader;
        private VorbisWaveReader oggReader;
        private Mp3FileReader mp3Reader;
        private UndertaleData audioGroupData;
        private string loadedPath;

        private static readonly MainWindow mainWindow = Application.Current.MainWindow as MainWindow;

        public UndertaleSoundEditor()
        {
            InitializeComponent();
            this.Unloaded += Unload;

            ((Image)mainWindow.FindName("Flowey")).Opacity = 0;
            ((Image)mainWindow.FindName("FloweyLeave")).Opacity = 0;
            ((Image)mainWindow.FindName("FloweyBubble")).Opacity = 0;

            ((Label)this.FindName("SoundsObjectLabel")).Content = ((Label)mainWindow.FindName("ObjectLabel")).Content;
        }

        private void UndertaleSoundsEditor_Unloaded(object sender, RoutedEventArgs e)
        {
            var floweranim = ((Image)mainWindow.FindName("Flowey"));
            //floweranim.Opacity = 1;

            var controller = ImageBehavior.GetAnimationController(floweranim);
            controller.Pause();
            controller.GotoFrame(controller.FrameCount - 5);
            controller.Play();

            ((Image)mainWindow.FindName("FloweyLeave")).Opacity = 0;
        }
        private void UserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            UndertaleSound code = this.DataContext as UndertaleSound;

            int foundIndex = code is UndertaleResource res ? mainWindow.Data.IndexOf(res, false) : -1;
            string idString;

            if (foundIndex == -1)
                idString = "None";
            else if (foundIndex == -2)
                idString = "N/A";
            else
                idString = Convert.ToString(foundIndex);

            ((Label)this.FindName("SoundsObjectLabel")).Content = idString;

            //((Image)mainWindow.FindName("FloweyBubble")).Opacity = 0;
            //((Image)mainWindow.FindName("Flowey")).Opacity = 0;
        }

        public void Unload(object sender, RoutedEventArgs e)
        {
            if (waveOut != null)
                waveOut.Stop();
        }

        private void InitAudio()
        {
            if (waveOut == null)
                waveOut = new WaveOutEvent() { DeviceNumber = 0 };
            else if (waveOut.PlaybackState != PlaybackState.Stopped)
                waveOut.Stop();
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            UndertaleSound sound = DataContext as UndertaleSound;

            if ((sound.Flags & UndertaleSound.AudioEntryFlags.IsEmbedded) != UndertaleSound.AudioEntryFlags.IsEmbedded &&
                (sound.Flags & UndertaleSound.AudioEntryFlags.IsCompressed) != UndertaleSound.AudioEntryFlags.IsCompressed)
            {
                try
                {
                    string filename;
                    if (!sound.File.Content.Contains("."))
                        filename = sound.File.Content + ".ogg";
                    else
                        filename = sound.File.Content;
                    string audioPath = Path.Combine(Path.GetDirectoryName((Application.Current.MainWindow as MainWindow).FilePath), filename);
                    if (File.Exists(audioPath))
                    {
                        switch (Path.GetExtension(filename).ToLower())
                        {
                            case ".wav":
                                wavReader = new WaveFileReader(audioPath);
                                InitAudio();
                                waveOut.Init(wavReader);
                                waveOut.Play();
                                break;
                            case ".ogg":
                                oggReader = new VorbisWaveReader(audioPath);
                                InitAudio();
                                waveOut.Init(oggReader);
                                waveOut.Play();
                                break;
                            case ".mp3":
                                mp3Reader = new Mp3FileReader(audioPath);
                                InitAudio();
                                waveOut.Init(mp3Reader);
                                waveOut.Play();
                                break;
                            default:
                                throw new Exception("Unknown file type.");
                        }
                    }
                    else
                        throw new Exception("Failed to find audio file.");
                } catch (Exception ex)
                {
                    waveOut = null;
                    mainWindow.ShowError("Failed to play audio!\r\n" + ex.Message, "Audio failure");
                }
                return;
            }

            UndertaleEmbeddedAudio target;

            if (sound.GroupID != 0 && sound.AudioID != -1)
            {
                try
                {
                    string relativePath;
                    if (sound.AudioGroup is UndertaleAudioGroup { Path.Content: string customRelativePath })
                    {
                        relativePath = customRelativePath;
                    }
                    else
                    {
                        relativePath = $"audiogroup{sound.GroupID}.dat";
                    }
                    string path = Path.Combine(Path.GetDirectoryName(mainWindow.FilePath), relativePath);
                    if (File.Exists(path))
                    {
                        if (loadedPath != path)
                        {
                            loadedPath = path;

                            using FileStream stream = new(path, FileMode.Open, FileAccess.Read);
                            audioGroupData = UndertaleIO.Read(stream, (warning, _) =>
                            {
                                throw new Exception(warning);
                            });
                        }

                        target = audioGroupData.EmbeddedAudio[sound.AudioID];
                    }
                    else
                        throw new Exception("Failed to find audio group file.");
                } catch (Exception ex)
                {
                    waveOut = null;
                    mainWindow.ShowError("Failed to play audio!\r\n" + ex.Message, "Audio failure");
                    return;
                }
            } else
                target = sound.AudioFile;

            if (target != null)
            {
                if (target.Data.Length > 4)
                {
                    try
                    {
                        if (target.Data[0] == 'R' && target.Data[1] == 'I' && target.Data[2] == 'F' && target.Data[3] == 'F')
                        {
                            wavReader = new WaveFileReader(new MemoryStream(target.Data));
                            InitAudio();
                            waveOut.Init(wavReader);
                            waveOut.Play();
                        }
                        else if (target.Data[0] == 'O' && target.Data[1] == 'g' && target.Data[2] == 'g' && target.Data[3] == 'S')
                        {
                            oggReader = new VorbisWaveReader(new MemoryStream(target.Data));
                            InitAudio();
                            waveOut.Init(oggReader);
                            waveOut.Play();
                        }
                        else
                            mainWindow.ShowError("Failed to play audio!\r\nNot a WAV or OGG.", "Audio failure");
                    }
                    catch (Exception ex)
                    {
                        waveOut = null;
                        mainWindow.ShowError("Failed to play audio!\r\n" + ex.Message, "Audio failure");
                    }
                }
            }
            else
                mainWindow.ShowError("Failed to play audio!\r\nNo options for playback worked.", "Audio failure");
        }


        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (waveOut != null)
                waveOut.Stop();
        }
    }
}

#pragma warning restore CA1416 // Validate platform compatibility
