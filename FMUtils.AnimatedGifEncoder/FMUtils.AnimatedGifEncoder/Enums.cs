using System;

namespace FMUtils.AnimatedGifEncoder
{
    public enum ColorQuantizationQuality
    {
        Best = 1,
        Good = 5,
        Reasonable = 10,
        Fast = 20,
    };

    public enum FrameDisposalMethod
    {
        Unspecified = 0,
        DoNotDispose = 1,
        RestoreBackgroundColor = 2,
        RestorePrevious = 3,
    };

    [FlagsAttribute]
    public enum FrameOptimization
    {
        None = 0,

        /// <summary>
        /// Checks each frame to see if it's identical to the previous frame.
        /// If so, it will extend the duration of the previous frame and skip writing the current frame.
        /// If there are many duplicate frames, this optimization can greatly speed up processing since fewer quantizations can be run.
        /// </summary>
        DiscardDuplicates = 1,

        /// <summary>
        /// Using an automatically generated transparency color, erases pixels in the current frame which are identical to the composite image at that point.
        /// If there are few changes between frames, this optimization can greatly reduce file size since LZW compresses this well.
        /// </summary>
        AutoTransparency = 2,

        /// <summary>
        /// When combined with AutoTransparency, shrinks the frame dimensions to exclude areas where the frame doesn't change.
        /// If changes between frames are limited contiguous areas, this optimization can reduce file size.
        /// </summary>
        ClipFrame = 4,

        /// <summary>
        /// Instead of running processing the frame and writing it to the output stream when it is added, this flag will wait to do that until Dispose()
        /// This is especially preferable if frames are being generated as they are added to the gif.
        /// </summary>
        DeferredProcessing = 8
    };
}
