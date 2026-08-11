using System;
using System.IO;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VideoShelf.Models;


namespace VideoShelf.Controls
{

    public partial class VideoCard : UserControl
    {

        public event EventHandler<VideoInfo>? PlayRequested;
        public event EventHandler<VideoInfo>? FavoriteChanged;
        public event EventHandler<VideoInfo>? PlaylistAddRequested;
        public event EventHandler<VideoSelectionChangedEventArgs>? SelectionChanged;

        private VideoInfo video;

        private Point startPoint;

        private bool isDragging;
        private readonly bool previewEnabled;
        private MediaElement? previewPlayer;


        public VideoCard(VideoInfo video, bool previewEnabled = false)
        {

            InitializeComponent();

            this.video = video;
            this.previewEnabled = previewEnabled;


            Refresh();

        }



        // 鼠标按下记录位置
        private void UserControl_MouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {

            startPoint =
                e.GetPosition(this);

            isDragging = false;

        }




        // 拖动检测
        private void UserControl_MouseMove(
            object sender,
            MouseEventArgs e)
        {

            if (e.LeftButton == MouseButtonState.Pressed)
            {

                Point current =
                    e.GetPosition(this);


                double distance =
                    Math.Abs(current.X - startPoint.X)
                    +
                    Math.Abs(current.Y - startPoint.Y);



                if (distance < 10)
                    return;

                isDragging = true;
                StartDrag();

            }

        }

        public void Refresh()
        {
            VideoName.Text = video.FileName;
            VideoDuration.Text = video.IsLoading ? "正在生成预览…" : video.DurationText;
            FavoriteButton.Content = video.IsFavorite ? "♥" : "♡";
            FavoriteButton.Foreground = new System.Windows.Media.SolidColorBrush(
                video.IsFavorite ? System.Windows.Media.Color.FromRgb(255, 76, 104) : System.Windows.Media.Color.FromRgb(232, 235, 242));
            FavoriteButton.ToolTip = video.IsFavorite ? "取消收藏" : "收藏";
            if (!File.Exists(video.ThumbnailPath)) return;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 240;
            bitmap.UriSource = new Uri(video.ThumbnailPath);
            bitmap.EndInit();
            Thumbnail.Source = bitmap;
        }

        public void DeactivatePreview()
        {
            previewPlayer?.Stop(); if (previewPlayer != null) previewPlayer.Source = null;
            PreviewHost.Children.Clear(); PreviewHost.Visibility = Visibility.Collapsed; previewPlayer = null; Thumbnail.Visibility = Visibility.Visible;
        }

        private void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            video.IsFavorite = !video.IsFavorite;
            Refresh();
            FavoriteChanged?.Invoke(this, video);
            e.Handled = true;
        }

        private void PlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistAddRequested?.Invoke(this, video);
            e.Handled = true;
        }

        private void SelectionCheck_Changed(object sender, RoutedEventArgs e)
        {
            SelectionChanged?.Invoke(this, new VideoSelectionChangedEventArgs(video, SelectionCheck.IsChecked == true));
            e.Handled = true;
        }


        private void UserControl_MouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {

            if (!isDragging)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    SelectionCheck.IsChecked = SelectionCheck.IsChecked != true;
                    e.Handled = true;
                }
                else PlayRequested?.Invoke(this, video);
            }

        }




        // 拖到mpv
        private void StartDrag()
        {

            StringCollection files =
                new StringCollection();


            files.Add(video.FilePath);



            DataObject data =
                new DataObject();


            data.SetFileDropList(files);



            DragDrop.DoDragDrop(
                this,
                data,
                DragDropEffects.Copy);

        }




        // 鼠标进入预览
        private void UserControl_MouseEnter(
            object sender,
            MouseEventArgs e)
        {

            if (!previewEnabled || string.IsNullOrEmpty(video.PreviewPath) || !File.Exists(video.PreviewPath))
                return;


            Thumbnail.Visibility =
                Visibility.Collapsed;


            previewPlayer = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = System.Windows.Media.Stretch.UniformToFill,
                Source = new Uri(video.PreviewPath)
            };
            PreviewHost.Children.Add(previewPlayer);
            PreviewHost.Visibility = Visibility.Visible;
            previewPlayer.Play();

        }




        // 鼠标离开恢复
        private void UserControl_MouseLeave(
            object sender,
            MouseEventArgs e)
        {

            previewPlayer?.Stop();
            if (previewPlayer != null) previewPlayer.Source = null;
            PreviewHost.Children.Clear();
            PreviewHost.Visibility = Visibility.Collapsed;
            previewPlayer = null;


            Thumbnail.Visibility =
                Visibility.Visible;

        }


    }

    public sealed record VideoSelectionChangedEventArgs(VideoInfo Video, bool IsSelected);

}
