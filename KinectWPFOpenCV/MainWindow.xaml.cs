using System;
using System.Collections.Generic;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Kinect;
using Emgu.CV;
using Emgu.CV.Structure;
using System.IO;
using Ventuz.OSC;

namespace KinectWPFOpenCV
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private KinectSensor sensor;
        private WriteableBitmap colorBitmap;
        private WriteableBitmap irBitmap;
        private WriteableBitmap blobsImageBitmap;
        private FormatConvertedBitmap convertedImage;
        private byte[] colorPixels;
        private byte[] irPixels;
        private bool IrEnabled = true;
        private bool switchImg = false;
        private Timer timer;
        private int reverseXMult = 1;
        private int reverseYMult = 1;

        //private Image<Gray, Byte> draw;
        private Image<Bgr, byte> openCVImg;
        private Image<Gray, byte> thresholdedImage;
        private float xPos;
        private float yPos;

        // Osc Members
        private string oscAddress = "127.0.0.1";
        private string oscPort = "9999";
        private static UdpWriter oscWriter;
        private static string[] oscArgs = new string[2];

        private static UdpReader oscReader;

        //private static BlobTrackerAuto<Bgr> _tracker;
        //private static IBGFGDetector<Bgr> _detector;
        //private static MCvFont _font = new MCvFont(Emgu.CV.CvEnum.FONT.CV_FONT_HERSHEY_SIMPLEX, 1.0, 1.0);

        private float movingAvFactor = 0.1f;
        private float oldX = 0;
        private float oldY = 0;

        private int blobCount = 0;
        private MCvBox2D box;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            //MouseDown += MainWindow_MouseDown;

        }


        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {

            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    sensor = potentialSensor;
                    break;
                } 
            }

            if (sensor != null)
            {
                // don't want to turn it off anymore
                // using it to create reflection
                //try
                //{
                //    sensor.ForceInfraredEmitterOff = true;
                //}
                //catch (Exception)
                //{
                //    Console.WriteLine("You can't turn off the infrared emitter on XBOX Kinect");
                //}
                sensor.ColorStream.Enable(ColorImageFormat.InfraredResolution640x480Fps30);
                irPixels = new byte[sensor.ColorStream.FramePixelDataLength];
                colorPixels = new byte[sensor.ColorStream.FramePixelDataLength*2];
                colorBitmap = new WriteableBitmap(sensor.ColorStream.FrameWidth, sensor.ColorStream.FrameHeight, 96.0,
                                                  96.0, PixelFormats.Bgr32, null);
                irBitmap = new WriteableBitmap(sensor.ColorStream.FrameWidth, sensor.ColorStream.FrameHeight, 96.0, 96.0,
                                               PixelFormats.Gray16, null);

                convertedImage = new FormatConvertedBitmap();

                sensor.AllFramesReady += sensor_AllFramesReady;

                //_detector = new FGDetector<Bgr>(FORGROUND_DETECTOR_TYPE.FGD);

                //_tracker = new BlobTrackerAuto<Bgr>();

                // Setup osc sender
                oscArgs[0] = oscAddress;
                oscArgs[1] = oscPort;
                oscWriter = new UdpWriter(oscArgs[0], Convert.ToInt32(oscArgs[1]));
                //oscWriter.Dispose();
                //oscWriter = new UdpWriter(oscArgs[0], Convert.ToInt32(oscArgs[1]));
                timer = new Timer();
                timer.Interval = 4000;
                timer.Elapsed += TimerOnElapsed;

                LoadSettingsFromFile();

                try
                {
                    sensor.Start();
                }
                catch (IOException)
                {
                    sensor = null;
                }

            }

            if (sensor == null)
            {
                //outputViewbox.Visibility = System.Windows.Visibility.Collapsed;
                txtMessage.Text = "No Kinect Found.\nPlease plug in Kinect\nand restart this application.";
            }

        }

        private void CreateImageForTracking(WriteableBitmap bitmap, ColorImageFrame colorFrame, byte[] pixels)
        {
            bitmap.WritePixels(
                new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight),
                pixels,
                bitmap.PixelWidth*colorFrame.BytesPerPixel,
                0);

            try
            {

                convertedImage = ImageHelpers.BitmapToFormat(bitmap, PixelFormats.Bgr32);
                openCVImg = new Image<Bgr, byte>(convertedImage.GetBitmap());
                thresholdedImage = openCVImg.Convert<Gray, byte>().ThresholdBinary(new Gray(thresholdValue.Value),
                                                                                   new Gray(255));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to create Emgu images");
                Console.WriteLine(ex);
            }
        }

        private void TrackBlobs()
        {
            try
            {
                using (MemStorage stor = new MemStorage())
                {
                    //Find contours with no holes try CV_RETR_EXTERNAL to find holes
                    Contour<System.Drawing.Point> contours = thresholdedImage.FindContours(
                        Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_SIMPLE,
                        Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_EXTERNAL,
                        stor);
                    int contourCounter = 0;


                    for (int i = 0; contours != null; contours = contours.HNext)
                    {
                        i++;

                        if ((contours.Area > Math.Pow(sliderMinSize.Value, 2)) &&
                            (contours.Area < Math.Pow(sliderMaxSize.Value, 2)))
                        {
                            box = contours.GetMinAreaRect();

                            openCVImg.Draw(box, new Bgr(System.Drawing.Color.Red), 2);
                            thresholdedImage.Draw(box, new Gray(255), 2);
                            blobCount++;
                            if (reverseX.IsChecked == true) {
                                reverseXMult = -1;
                            }
                            else
                            {
                                reverseXMult = 1;
                            }
                            if (reverseY.IsChecked == true) {
                                reverseYMult = -1;
                            }
                            else
                            {
                                reverseYMult = 1;
                            }
                            var x = box.center.X/sensor.ColorStream.FrameWidth;
                            var y = box.center.Y/sensor.ColorStream.FrameHeight;
                            xPos = (reverseXMult * (x - (float)centerXOffset.Value) + (reverseXMult * (float)xOffset.Value * x)) * (float)xMultiplier.Value ;
                            yPos = (reverseYMult * (y - (float)centerYOffset.Value) + (reverseYMult * (float)yOffset.Value * y)) * (float)yMultiplier.Value;
                            
                            /* calculating moving avarage */
                            var smoothFactor = (float)smoothingFactor.Value / 100;
                            oldX = (1 - smoothFactor) * xPos + oldX * smoothFactor;
                            oldY = (1 - smoothFactor) * yPos + oldY * smoothFactor;

                            // send osc data
                            try
                            {
                                SendOsc(oldX, oldY);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Failed to send osc: ");
                                Console.WriteLine(ex);
                            }

                        }
                    }
                }
            }
            catch
            {
                Console.WriteLine("Failed to do tracking");
            }


        }

        private void sensor_AllFramesReady(object sender, AllFramesReadyEventArgs e)
        {

            using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
            {
                if (colorFrame != null)
                {
                    //depthBmp = depthFrame.SliceDepthImage((int)sliderMin.Value, (int)sliderMax.Value);
                    blobCount = 0;

                    if (IrEnabled && colorFrame.Format == ColorImageFormat.InfraredResolution640x480Fps30)
                    {
                        colorFrame.CopyPixelDataTo(irPixels);
                        CreateImageForTracking(irBitmap, colorFrame, irPixels);
                        TrackBlobs();
                    }
                    else if (!IrEnabled && colorFrame.Format == ColorImageFormat.RgbResolution640x480Fps30)
                    {
                        colorFrame.CopyPixelDataTo(colorPixels);
                        //CreateImageForTracking(colorBitmap, colorFrame, colorPixels);
                        colorBitmap.WritePixels(
                            new Int32Rect(0, 0, colorBitmap.PixelWidth, colorBitmap.PixelHeight),
                            colorPixels,
                            colorBitmap.PixelWidth*colorFrame.BytesPerPixel,
                            0);
                        mainImg.Source = colorBitmap;
                        return;
                    }
                    else
                    {
                        return;
                    }


                    //openCVImg.Save("c:\\opencvImage.bmp");

                    if (switchImg)
                    {
                        mainImg.Source = ImageHelpers.ToBitmapSource(thresholdedImage);
                        secondaryImg.Source = ImageHelpers.ToBitmapSource(openCVImg);
                    }
                    else
                    {
                        mainImg.Source = ImageHelpers.ToBitmapSource(openCVImg);
                        secondaryImg.Source = ImageHelpers.ToBitmapSource(thresholdedImage);
                    }
                    txtBlobCount.Text = blobCount.ToString();
                }
            }
        }

        private void ToggleIrColor(object sender, RoutedEventArgs e)
        {
            IrEnabled = !IrEnabled;
            if (IrEnabled)
            {
                sensor.ColorStream.Enable(ColorImageFormat.InfraredResolution640x480Fps30);
                txtMessage.Text = "InfraRed Enabled\nTracking On";
                SwitchImgBtn.IsEnabled = true;
                timer.Start();
            }
            else
            {
                sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                txtMessage.Text = "Color Enabled\nTracking Off";
                SwitchImgBtn.IsEnabled = false;
                timer.Start();
            }
        }

        private void SwitchImg(object sender, RoutedEventArgs e)
        {
            switchImg = !switchImg;
        }

        private void SaveSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                string settings = xMultiplier.Value.ToString() + "|" + yMultiplier.Value.ToString() + "|"
                    + xOffset.Value.ToString() + "|" + yOffset.Value.ToString() + "|" 
                    + centerXOffset.Value.ToString() + "|" + centerYOffset.Value.ToString() + "|"
                    + reverseX.IsChecked.ToString() + "|" + reverseY.IsChecked.ToString() + "|"
                    + thresholdValue.Value.ToString() + "|"
                    + sliderMinSize.Value.ToString() + "|" + sliderMaxSize.Value.ToString() + "|" + smoothingFactor.Value.ToString();
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"Settings.txt", false))
                {
                    file.WriteLine(settings);
                }
                txtMessage.Text = "Settings Saved.";
                
                timer.Start();
            }
            catch
            {
                txtMessage.Text = "There was an error\nsaving the settings to the file.\nIs the file locked?";
            }

        }

        private delegate void ClearTextMessage();

        private void TimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            this.Dispatcher.Invoke(DispatcherPriority.Normal, new ClearTextMessage(ClearMessage));
            timer.Stop();
        }

        private void ClearMessage()
        {
            txtMessage.Text = "";
        }

        private void LoadSettings(object sender, RoutedEventArgs e)
        {
            LoadSettingsFromFile();
        }

        private void LoadSettingsFromFile()
        {
            //_timeOfMessage = Time.time;
            //_elapsedTime = (int)_timeOfMessage + _messageTime;
            try {
                string data = System.IO.File.ReadAllText(@"Settings.txt");
                string[] values = data.Split('|');
                xMultiplier.Value = int.Parse(values[0]);
                yMultiplier.Value = int.Parse(values[1]);
                xOffset.Value = Math.Round(float.Parse(values[2]), 2);
                yOffset.Value = Math.Round(float.Parse(values[3]), 2);
                centerXOffset.Value = Math.Round(float.Parse(values[4]), 1);
                centerYOffset.Value = Math.Round(float.Parse(values[5]), 1);
                reverseX.IsChecked = bool.Parse(values[6]);
                reverseY.IsChecked = bool.Parse(values[7]);
                thresholdValue.Value = int.Parse(values[8]);
                sliderMinSize.Value = int.Parse(values[9]);
                sliderMaxSize.Value = int.Parse(values[10]);
                smoothingFactor.Value = int.Parse(values[11]);
                txtMessage.Text = "Settings Loaded.";
                timer.Start();
            }
            catch {
                txtMessage.Text = "There was an error\nloading the settings from the file.\nIs the file missing?";
            }
        }


        /// <summary>
        /// Sends Osc on the global port
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private void SendOsc(float x, float y)
        {
            // send osc data
            var elements = new List<OscElement>();
            var address = "/bait";
            elements.Add(new OscElement(address + "/x", x));
            elements.Add(new OscElement(address + "/y", y));
            oscWriter.Send(new OscBundle(DateTime.Now, elements.ToArray()));
        }

        #region processing a different way - not used

        //private void ProcessFrame(Image<Gray, byte> gray_image)
        //{
        //    Image<Gray, Byte> frame1 = gray_image.ThresholdBinary(new Gray(254), new Gray(255));

        //    //find Canny edges
        //    Image<Gray, Byte> canny = frame1.Canny(new Gray(255), new Gray(255));

        //    // If drawing for the first time, initialize "diff", else draw on it
        //    if (first) {
        //        draw = new Image<Gray, Byte>(frame1.Width, frame1.Height, new Gray(0));
        //        //If you take you LED into this rectangle (at the bottom left corner),
        //        //then the screen will refresh and all your markings will be cleared
        //        draw.Draw(new Rectangle(0, 455, 25, 25), new Gray(255), 0);
        //        first = !first;
        //    }
        //    else {
        //        //In this loop, we find contours of the canny image and using the
        //        //Bounding Rectangles, we find the location of the LED(s)
        //        for (Contour<System.Drawing.Point> contours = canny.FindContours(
        //                  Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_CHAIN_APPROX_SIMPLE,
        //                  Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_EXTERNAL); contours != null; contours = contours.HNext) {
        //            //Check if LED(s) point lies in the region of refreshing the screen
        //            if (contours.BoundingRectangle.X <= 25 && contours.BoundingRectangle.Y >= 455) {
        //                first = true;
        //                break;
        //            }
        //            else {
        //                Point pt = new Point(contours.BoundingRectangle.X, contours.BoundingRectangle.Y);
        //                draw.Draw(new CircleF(pt, 5), new Gray(255), 0);
        //                canny.Draw(contours.BoundingRectangle, new Gray(255), 1);
        //            }
        //        }

        //    }

        //}

        //public static Image<Bgr, byte> ProcessFrame(BitmapSource irFrame)
        //{
        //    Image<Bgr, byte> frame = new Image<Bgr, byte>(irFrame.ToBitmap());
        //    frame._SmoothGaussian(3); //filter out noises

        //    #region use the BG/FG detector to find the forground mask
        //    _detector.Update(frame);
        //    Image<Gray, Byte> forgroundMask = _detector.ForgroundMask;
        //    #endregion

        //    _tracker.Process(frame, forgroundMask);

        //    foreach (MCvBlob blob in _tracker) {
        //        frame.Draw((System.Drawing.Rectangle)blob, new Bgr(255.0, 255.0, 255.0), 2);
        //        frame.Draw(blob.ID.ToString(), ref _font, System.Drawing.Point.Round(blob.Center), new Bgr(255.0, 255.0, 255.0));
        //    }
        //    return frame;
        //}

        #endregion

        #region Window Stuff

        //void MainWindow_MouseDown(object sender, MouseButtonEventArgs e)
        //{
        //    //this.DragMove();
        //}


        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (sensor != null)
            {
                sensor.Stop();
            }
        }


        //private void CloseBtnClick(object sender, RoutedEventArgs e)
        //{
        //    Console.WriteLine("CLosed window");
        //    this.Close();
        //}

        #endregion
    }
}
