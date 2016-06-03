using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Kinect;
using Microsoft.Kinect.VisualGestureBuilder;
using Rug.Osc;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace KinectOSC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        KinectSensor kinect;

        // Color
        ColorFrameReader colorFrameReader;
        FrameDescription colorFrameDesc;
        ColorImageFormat colorFormat = ColorImageFormat.Bgra;

        // Body
        int BODY_COUNT;
        BodyFrameReader bodyFrameReader;
        Body[] bodies;

        // Gesture
        VisualGestureBuilderFrameReader[] gestureFrameReaders;
        IReadOnlyList<Gesture> gestures;

        // OSC
        OscSender oscSender;
        IPAddress sendAddress;
        ushort    sendPort;

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

                colorStride = colorFrameDesc.Width * (int)colorFrameDesc.BytesPerPixel;
                colorRect   = new Int32Rect(0, 0, colorFrameDesc.Width, colorFrameDesc.Height);
                colorBuffer = new byte[colorStride * colorFrameDesc.Height];
                ImageColor.Source = colorBitmap;

                // Bodyの最大数を取得する
                BODY_COUNT = kinect.BodyFrameSource.BodyCount;

                // Bodyを入れる配列を作る
                bodies = new Body[BODY_COUNT];

                // ボディーリーダーを開く
                bodyFrameReader = kinect.BodyFrameSource.OpenReader();
                bodyFrameReader.FrameArrived += bodyFrameReader_FrameArrived;

                InitializeGesture();

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
        private void DrawBodyFrame()
        {
            CanvasBody.Children.Clear();

            // 追跡しているBodyのみループする
            foreach (var body in bodies.Where( b => b.IsTracked ))
            {
                foreach (var joint in body.Joints)
                {
                    // 位置が追跡状態
                    if (joint.Value.TrackingState == TrackingState.Tracked)
                    {
                        DrawEllipse(joint.Value, 30, Brushes.Blue);
                    }

                    // 位置が推測状態
                    else if (joint.Value.TrackingState == TrackingState.Inferred)
                    {
                        DrawEllipse(joint.Value, 30, Brushes.Yellow);
                    }
                }
            }
        }

        private void DrawEllipse(Joint joint, int R, Brush brush)
        {
            var ellipse = new Ellipse()
            {
                Width = R,
                Height = R,
                Fill = brush,
            };

            // カメラ座標系をDepth座標系に変換する.
            //var point = kinect.CoordinateMapper.MapCameraPointToDepthSpace(joint.Position);
            var point = kinect.CoordinateMapper.MapCameraPointToColorSpace(joint.Position);
            if ((point.X < 0) || (point.Y < 0))
            {
                return;
            }

            // Depth座標系で円を配置する
            Canvas.SetLeft(ellipse, point.X - (R / 2));
            Canvas.SetTop(ellipse, point.Y - (R / 2));

            CanvasBody.Children.Add(ellipse);
        }

        private void bodyFrameReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            UpdateBodyFrame();
        }

        private void InitializeGesture()
        {
            gestureFrameReaders = new VisualGestureBuilderFrameReader[BODY_COUNT];

            // BodyごとにGesture Frame Readerを開く
            for (int count = 0; count < BODY_COUNT; count++)
            {
                VisualGestureBuilderFrameSource gestureFrameSource = new VisualGestureBuilderFrameSource(kinect, 0);
                gestureFrameReaders[count] = gestureFrameSource.OpenReader();
                gestureFrameReaders[count].FrameArrived += gestureFrameReaders_FrameArrived;
            }

            // .gbdファイルからジェスチャーデータベースを作成
            VisualGestureBuilderDatabase gestureDatabase = new VisualGestureBuilderDatabase("GestureDictionary.gbd");

            // データベースからジェスチャーを取得してGesture Frame Readerにそれぞれ登録
            gestures = gestureDatabase.AvailableGestures;
            for (int count = 0; count < BODY_COUNT; count++)
            {
                VisualGestureBuilderFrameSource gestureFrameSource = gestureFrameReaders[count].VisualGestureBuilderFrameSource;
                gestureFrameSource.AddGestures(gestures);
                foreach (var g in gestures)
                {
                    gestureFrameSource.SetIsEnabled(g, true);
                }
            }
        }

        private void gestureFrameReaders_FrameArrived(object sender, VisualGestureBuilderFrameArrivedEventArgs e)
        {
            VisualGestureBuilderFrame gestureFrame = e.FrameReference.AcquireFrame();
            if (gestureFrame == null) { return; }
            UpdateGestureFrame( gestureFrame );
            gestureFrame.Dispose();
        }
        private void UpdateGestureFrame(VisualGestureBuilderFrame gestureFrame)
        {
            // Tracking IDの登録確認
            bool tracked = gestureFrame.IsTrackingIdValid;
            if (!tracked) { return; }

            // ジェスチャーの認識結果を取得
            foreach (var g in gestures)
            {
                Result(gestureFrame, g);
            }
        }
        private void UpdateBodyFrame()
        {
            if (bodyFrameReader == null) { return; }
            BodyFrame bodyFrame = bodyFrameReader.AcquireLatestFrame();
            if (bodyFrame == null){ return; }

            bodyFrame.GetAndRefreshBodyData(bodies);

            for (int count = 0; count < BODY_COUNT; count++)
            {
                Body body = bodies[count];
                bool tracked = body.IsTracked;
                if (!tracked) { continue; }
                ulong trackingId = body.TrackingId;
                VisualGestureBuilderFrameSource gestureFrameSource;
                gestureFrameSource = gestureFrameReaders[count].VisualGestureBuilderFrameSource;
                gestureFrameSource.TrackingId = trackingId;
                DrawBodyFrame();
            }
            bodyFrame.Dispose();
        }
        private Body ChooseClosestBody(Body[] bodies,
            CameraSpacePoint closestPerson = new CameraSpacePoint(), float closestDistance = 2.0f)
        {
            Body closestBody = null;
            var baseType = JointType.SpineBase;

            // 追跡しているBodyから選ぶ
            foreach (var body in bodies.Where(b => b.IsTracked))
            {
                // 比較する関節位置が追跡状態になければ対象外
                if (body.Joints[baseType].TrackingState == TrackingState.NotTracked) { continue; }

                // 中心からの距離が近い人を選ぶ
                var distance = Distance(closestPerson, body.Joints[baseType].Position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestBody = body;
                }
            }

            return closestBody;
        }

        float Distance (CameraSpacePoint p1, CameraSpacePoint p2)
        {
            return (float)Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) +
                                    (p2.Y - p1.Y) * (p2.Y - p1.Y) +
                                    (p2.Z - p1.Z) * (p2.Z - p1.Z));
        }

        private void Result(VisualGestureBuilderFrame gestureFrame, Gesture gesture)
        {
            // どのReaderが取得したFrameかIndexを取得
            int count = GetIndexofGestureReader(gestureFrame);
            GestureType gestureType = gesture.GestureType;

            List<OscMessage> messages = new List<OscMessage>();

            switch (gestureType)
            {
                case GestureType.Discrete:
                    DiscreteGestureResult dGestureResult = gestureFrame.DiscreteGestureResults[gesture];

                    bool detected = dGestureResult.Detected;
                    if (!detected) { return; }

                    float confidence = dGestureResult.Confidence;

                    if (bodies[count] == ChooseClosestBody(bodies, new CameraSpacePoint()))
                    {
                        Console.WriteLine($"dGesture: {gesture.Name} , {confidence.ToString()}");
                        messages.Add(new OscMessage("/dGesture", $"{gesture.Name}", confidence));
                    }

                    break;

                case GestureType.Continuous:
                    ContinuousGestureResult cGestureResult = gestureFrame.ContinuousGestureResults[gesture];

                    float progress = cGestureResult.Progress;

                    if (bodies[count] == ChooseClosestBody(bodies, new CameraSpacePoint()))
                    {
                        Console.WriteLine($"cGesture: {gesture.Name} , {progress.ToString()}");
                        messages.Add(new OscMessage("/cGesture", $"/{gesture.Name}", progress));
                    }

                    break;

                default:
                    break;
            }

            if (sendAddress != null && sendPort.ToString() != "")
            {
                using (oscSender = new OscSender(sendAddress, sendPort))
                {
                    oscSender.Connect();

                    foreach (var m in messages)
                    {
                        oscSender.Send(m);
                    }
                }
            }
        }
        private int GetIndexofGestureReader(VisualGestureBuilderFrame gestureFrame)
        {
            for (int index = 0; index < BODY_COUNT; index++)
            {
                if (gestureFrame.TrackingId
                    == gestureFrameReaders[index].VisualGestureBuilderFrameSource.TrackingId)
                {
                    return index;
                }
            }
            return -1;
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
                sendAddress = IPAddress.Parse(string.Join(".", ip));
                sendPort    = (ushort)int.Parse(Port.Text.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラーが発生しました：{ex.Message.ToString()}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
