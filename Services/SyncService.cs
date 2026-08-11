using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoShelf.Models;


namespace VideoShelf.Services
{

    public class SyncService
    {


        private readonly QuickScanner scanner =
            new QuickScanner();





        /// <summary>
        /// 同步视频库
        /// </summary>
        public async Task<List<VideoInfo>> Sync(
            VideoCache cache, long minimumFileSizeBytes = 0, double minimumDurationSeconds = 0)
        {

            return await Task.Run(() =>
            {

                return QuickSync(cache, minimumFileSizeBytes, minimumDurationSeconds);

            });

        }







        /// <summary>
        /// 快速扫描同步
        /// </summary>
        public List<VideoInfo> QuickSync(
            VideoCache cache, long minimumFileSizeBytes = 0, double minimumDurationSeconds = 0)
        {


            //没有目录
            if (string.IsNullOrEmpty(
                cache.RootFolder))
            {

                return cache.Videos;

            }




            //目录不存在

            if (!Directory.Exists(
                cache.RootFolder))
            {

                return new List<VideoInfo>();

            }





            //快速扫描文件

            List<string> files =
                scanner.ScanFiles(
                    cache.RootFolder);






            List<VideoInfo> result =
                new List<VideoInfo>();







            foreach (string file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length < minimumFileSizeBytes) continue;


                //查找旧缓存

                VideoInfo? old =
                    cache.Videos
                    .FirstOrDefault(
                        x =>
                        x.FilePath.Equals(
                            file,
                            StringComparison.OrdinalIgnoreCase));






                if (old != null)
                {
                    if (old.Duration.TotalSeconds > 0 && old.Duration.TotalSeconds < minimumDurationSeconds) continue;

                    //文件还存在
                    //直接保留缓存

                    // Failed or interrupted thumbnail jobs must be retried on
                    // the next quick sync instead of remaining blank forever.
                    DateTime sourceWriteTime = File.GetLastWriteTimeUtc(file);
                    bool sourceChanged = old.SourceLastWriteTime != default && old.SourceLastWriteTime != sourceWriteTime;
                    if (sourceChanged) old.ThumbnailFailed = false;
                    // Old versions persisted a permanent failure without an
                    // error reason. Retry those legacy records once using the
                    // current probing and frame-selection pipeline.
                    if (old.ThumbnailFailed && string.IsNullOrWhiteSpace(old.ThumbnailError))
                        old.ThumbnailFailed = false;
                    old.SourceLastWriteTime = sourceWriteTime;
                    old.FileSize = fileInfo.Length;
                    old.IsLoading = !old.ThumbnailFailed && (string.IsNullOrWhiteSpace(old.ThumbnailPath)
                        || !File.Exists(old.ThumbnailPath)
                        || new FileInfo(old.ThumbnailPath).Length < 128
                        || (old.IsAudio && old.Duration <= TimeSpan.Zero));
                    result.Add(old);

                }
                else
                {

                    //发现新视频

                    VideoInfo video =
                        new VideoInfo
                        {

                            FilePath = file,


                            FileName =
                                Path.GetFileName(file),

                            SourceLastWriteTime = File.GetLastWriteTimeUtc(file),
                            FileSize = fileInfo.Length,
                            CreatedTime = fileInfo.CreationTime,



                            IsLoading = true

                        };


                    result.Add(video);

                }


            }






            return result;


        }



    }

}
