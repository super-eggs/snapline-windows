using System.Drawing;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal static class AppIcon
    {
        internal static BitmapFrame WindowImage()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.AppIcon.ico"))
            {
                var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                image.Freeze();
                return image;
            }
        }

        internal static Icon TrayImage()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Snapline.AppIcon.ico"))
            using (var icon = new Icon(stream, new Size(32, 32)))
                return (Icon)icon.Clone();
        }
    }
}
