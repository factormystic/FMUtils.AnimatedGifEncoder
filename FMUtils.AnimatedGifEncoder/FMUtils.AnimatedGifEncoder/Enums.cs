using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
}
