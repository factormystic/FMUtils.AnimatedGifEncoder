using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FMUtils.AnimatedGifEncoder;

namespace Demo
{
    class Program
    {
        static void Main(string[] args)
        {
            //var path = Path.Combine(Environment.ExpandEnvironmentVariables("%home%"), @"Desktop\prosnap-debug-test");
            var path = @"..\..\..\..\animated-gif-encoder\bin\Debug\test-data";

            var start = DateTime.UtcNow;

            using (var ms = new MemoryStream())
            {
                //using (var gif = new Gif89a(ms, optimization: FrameOptimization.DiscardDuplicates | FrameOptimization.AutoTransparency | FrameOptimization.ClipFrame | FrameOptimization.DeferredProcessing))
                using (var gif = new Gif89a(ms, optimization: FrameOptimization.DiscardDuplicates | FrameOptimization.AutoTransparency | FrameOptimization.ClipFrame))
                //using (var gif = new Gif89a(ms, optimization: FrameOptimization.DiscardDuplicates | FrameOptimization.AutoTransparency))
                //using (var gif = new Gif89a(ms, optimization: FrameOptimization.DiscardDuplicates))
                //using (var gif = new Gif89a(ms, optimization: FrameOptimization.None))
                {
                    var i = 0;

                    //foreach (var f in new DirectoryInfo(path).GetFiles("*manycolors.bmp").OrderBy(f => Convert.ToInt32(new Regex(@"^\d+").Match(f.Name).Groups[0].Value))
                    foreach (var f in new DirectoryInfo(path).GetFiles("*test.bmp").OrderBy(f => Convert.ToInt32(new Regex(@"^\d+").Match(f.Name).Groups[0].Value)).ToList()
                        //foreach (var f in new DirectoryInfo(path).GetFiles("*test-problem.bmp").OrderBy(f => Convert.ToInt32(new Regex(@"^\d+").Match(f.Name).Groups[0].Value)).ToList()
                        //foreach (var f in new DirectoryInfo(path).GetFiles("*singlecolor.bmp").OrderBy(f => Convert.ToInt32(new Regex(@"^\d+").Match(f.Name).Groups[0].Value)).ToList()
                        //.Take(3))

                        //.Skip(5)
                        //.Take(30)

                        //.Skip(10)
                        //.Take(2))

                        //.Take(2)
                        )

                    //foreach (var f in new DirectoryInfo(path).GetFiles("quantizer-test*.bmp"))

                    //foreach (var f in new DirectoryInfo(path).GetFiles("*singlecolor.bmp"))
                    {
                        gif.AddFrame(new Frame(f.FullName, quality: ColorQuantizationQuality.Best));
                    }
                }

                var fs = new FileStream(@"test-" + DateTime.Now.ToString("yyyy-MM-dd, hh-mm-ss") + " [" + (DateTime.UtcNow - start).TotalMilliseconds.ToString() + "].gif", FileMode.Create, FileAccess.ReadWrite);
                ms.WriteTo(fs);
            }
        }
    }
}
