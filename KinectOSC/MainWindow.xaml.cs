using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Kinect;
using Rug.Osc;
using System.Net;

namespace KinectOSC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        KinectSensor kinect;
        ColorFrameReader colorFrameReader;
        FrameDescription colorFrameDesc;
        ColorImageFormat colorFormat = ColorImageFormat.Bgra;

        // WPF
        WriteableBitmap colorBitmap;
        byte[] colorBuffer;
        int colorStride;
        Int32Rect colorRect;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                kinect = KinectSensor.GetDefault();
                if (kinect == null) { throw new Exception("Kinectを開けません"); }
                kinect.Open();

                // カラー画像の情報を作成(BGRA)
                colorFrameDesc = kinect.ColorFrameSource.CreateFrameDescription(colorFormat);

                // カラーリーダーを開く
                colorFrameReader = kinect.ColorFrameSource.OpenReader();
                colorFrameReader.FrameArrived += colorFrameReader_FrameArrived;

                // カラー用のビットマップを作成
                colorBitmap = new WriteableBitmap(colorFrameDesc.Width, colorFrameDesc.Height,
                                                  96, 96, PixelFormats.Bgra32, null);
                ImageColor.Source = colorBitmap;

                colorStride = colorFrameDesc.Width * (int)colorFrameDesc.BytesPerPixel;
                colorRect   = new Int32Rect(0, 0, colorFrameDesc.Width, colorFrameDesc.Height);
                colorBuffer = new byte[colorStride * colorFrameDesc.Height];
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                Close();
            }
        }

        private void colorFrameReader_FrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            UpdateColorFrame(e);
            DrawColorFrame();
        }

        private void UpdateColorFrame(ColorFrameArrivedEventArgs args)
        {
            // カラーフレームを取得
            using (var colorFrame = args.FrameReference.AcquireFrame())
            {
                if (colorFrame == null) { return; }

                // BGRAデータを取得する
                colorBuffer = new byte[colorFrameDesc.LengthInPixels * colorFrameDesc.BytesPerPixel];
                colorFrame.CopyConvertedFrameDataToArray(colorBuffer, ColorImageFormat.Bgra);
            }
        }

        private void DrawColorFrame()
        {
            // ビットマップにする
            colorBitmap.WritePixels(colorRect, colorBuffer, colorStride, 0);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (kinect != null)
            {
                kinect.Close();
                kinect = null;
            }
        }

        private void ButtonSetIP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string[] ip = { IP1.Text.ToString(), IP2.Text.ToString(), IP3.Text.ToString(), IP4.Text.ToString() };
                IPAddress sendAddress = IPAddress.Parse(string.Join(".", ip));
                using (OscSender oscSender = new OscSender(sendAddress, int.Parse(Port.Text.ToString())))
                {
                    oscSender.Connect();
                    oscSender.Send(new OscMessage("/msg", 1, 2));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラーが発生しました：{ex.Message.ToString()}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
