FMUtils.AnimatedGifEncoder
====================

_Based on [Kevin Weiner's gif encoder for Java](http://www.java2s.com/Code/Java/2D-Graphics-GUI/AnimatedGifEncoder.htm), rewritten for C#_


### Usage

```csharp
using FMUtils.AnimatedGifEncoder;

// given pic0.png .. pic99.png...

using (var fs = new FileStream("cool.gif", FileMode.Create, FileAccess.ReadWrite)) {
  using (var gif = new Gif89a(fs)) {
    for (int i = 0; i < 100; i++) {
      gif.AddFrame(new Frame($"pic{ i }.png", delay: 100, quality: ColorQuantizationQuality.Fast));
    }
  }
}

// ...and now, cool.gif exists & is your pics, animated!
```
