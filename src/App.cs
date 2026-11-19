using System;
using System.Collections.Generic;
using System.Windows;

namespace FloatLeaf
{
    internal static class App
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var prefs = ReaderStore.Load();
            ReaderText.English = prefs.English;
            var window = new ReaderWindow(prefs);
            application.MainWindow = window;
            if (args.Length > 0 && !String.IsNullOrWhiteSpace(args[0]))
                window.Loaded += delegate { window.OpenBook(args[0]); };
            application.Run(window);
        }

        internal static EpubBook Sample()
        {
            return new EpubBook("窗边手记", new List<EpubChapter> {
                new EpubChapter("雨停在窗边", new List<string> {
                    "雨停的时候，我还在读上一段的最后一句。水珠从窗框的拐角落下，敲在楼下的铁皮信箱上。隔了几秒，又是一声。",
                    "对面阳台有人把椅子搬了出来。他用袖口擦了擦椅面，却没有坐下，只把一盆薄荷放了上去。它大概已经等了一整个上午的太阳。",
                    "我把书往窗边移了半寸。纸上那块斜斜的亮光，也刚好落到了下一行。"
                }),
                new EpubChapter("旧车票", new List<string> {
                    "书翻到一半，有张旧车票滑了出来。蓝色的油墨已经淡了，背面还留着我用铅笔画的一个小圈。",
                    "我想了好一会儿，才记起那个下午。靠窗的座位有点晒，邻座的小孩一路数桥。数到第七座，他睡着了。",
                    "我把车票夹回原处。下一次翻到这里，也许还能想起第八座桥的样子。"
                }),
                new EpubChapter("夜里的灯", new List<string> {
                    "夜里十一点，街角的修鞋铺还亮着灯。师傅低着头，把最后一根线慢慢收紧。他抬起鞋底，在手心里轻轻拍了两下。",
                    "我在路口停了一会儿，看见他摘下眼镜，把灯绳一拉。那扇小窗暗下去，路边的梧桐倒显得比刚才更亮了。"
                })
            });
        }
    }
}
