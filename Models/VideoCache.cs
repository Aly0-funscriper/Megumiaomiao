using System;
using System.Collections.Generic;


namespace VideoShelf.Models
{

    public class VideoCache
    {

        //视频目录
        public string RootFolder { get; set; } = "";


        //视频列表
        public List<VideoInfo> Videos { get; set; }
            = new List<VideoInfo>();


        //更新时间
        public DateTime UpdateTime { get; set; }

    }

}