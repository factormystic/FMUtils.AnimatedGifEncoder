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

    public enum DisposalMethod
    {
        Unspecified = 0,
        DoNotDispose = 1,
        RestoreBackgroundColor = 2,
        RestorePrevious = 3,
    };

    [FlagsAttribute]
    public enum FrameOptimization
    {
        None,

        /// <summary>
        /// Checks each frame to see if it's identical to the previous frame.
        /// If so, it will extend the duration of the previous frame and skip writing the current frame.
        /// If there are many duplicate frames, this optimization can greatly speed up processing since fewer quantizations can be run.
        /// </summary>
        DiscardDuplicates,

        /// <summary>
        /// Using an automatically generated transparency color, erases pixels in the current frame which are identical to the composite image at that point.
        /// If there are few changes between frames, this optimization can greatly reduce file size since LZW compresses this well.
        /// </summary>
        AutoTransparency,
    };
}
