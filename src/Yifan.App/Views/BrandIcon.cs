using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Yifan.Views;

internal static class BrandIcon
{
    public static void Apply(Image image)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "yifan-32.png");
        image.Source = new BitmapImage(new Uri(path));
    }
}
