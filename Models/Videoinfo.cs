using System;


namespace VideoShelf.Models
{

    public class VideoInfo
    {

        public string FilePath { get; set; } = "";


        public string FileName { get; set; } = "";


        //文件大小
        public long FileSize { get; set; }


        //创建时间
        public DateTime CreatedTime { get; set; }



        public TimeSpan Duration { get; set; }



        public string DurationText
        {
            get
            {

                if (Duration.TotalHours >= 1)
                {
                    return Duration.ToString(
                        @"hh\:mm\:ss");
                }


                return Duration.ToString(
                    @"mm\:ss");

            }
        }



        public int Width { get; set; }


        public int Height { get; set; }



        public string ThumbnailPath { get; set; } = "";


        public string PreviewPath { get; set; } = "";


        public bool IsLoading { get; set; }

        public bool IsFavorite { get; set; }
        public bool ThumbnailFailed { get; set; }
        public bool ThumbnailQualityChecked { get; set; }
        public string ThumbnailError { get; set; } = "";
        public DateTime SourceLastWriteTime { get; set; }
        public bool IsAudio => new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".ape", ".alac", ".aiff", ".aif" }
            .Contains(System.IO.Path.GetExtension(FilePath), StringComparer.OrdinalIgnoreCase);
        public string LrcPath { get; set; } = "";


    }

}
