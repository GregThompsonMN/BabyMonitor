//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs">
//     Copyright (c) Greg Thompson 2015.  All rights reserved.
//     TODO: Determine appropriate License.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Kinect;
using Microsoft.Kinect.Face;

namespace BabyMonitor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>
        /// Size of the RGB pixel in the bitmap
        /// </summary>
        private readonly int bytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

        /// <summary>
        /// Kinect Sensor
        /// </summary>
        private KinectSensor kinectSensor = null;

        ///// <summary>
        ///// Coordinate Mapper for sychnorizing data from different sources or in different coordinate systems
        ///// </summary>
        //private CoordinateMapper coordinateMapper = null;

        ///// <summary>
        ///// The reader for all data arriving from the Kinect
        ///// My initial goal will be to include all data I think may at some point be useful
        ///// </summary>
        //private MultiSourceFrameReader multiSourceFrameReader = null;


        /// <summary>
        /// Maximum value (as a float) that can be returned by the InfraredFrame
        /// </summary>
        private const float InfraredSourceValueMaximum = (float)ushort.MaxValue;

        /// <summary>
        /// The value by which the infrared source data will be scaled
        /// </summary>
        private const float InfraredSourceScale = 0.75f;

        /// <summary>
        /// Smallest value to display when the infrared data is normalized
        /// </summary>
        private const float InfraredOutputValueMinimum = 0.01f;

        /// <summary>
        /// Largest value to display when the infrared data is normalized
        /// </summary>
        private const float InfraredOutputValueMaximum = 1.0f;

        /// <summary>
        /// Reader for infrared frames
        /// </summary>
        private InfraredFrameReader infraredFrameReader = null;

        private FrameDescription infFrameDescription = null;

        private ColorFrameReader colorFrameReader = null;

        private BodyFrameReader bodyFrameReader = null;
        /// <summary>
        /// Array to store bodies
        /// </summary>
        private Body[] bodies = null;

        /// <summary>
        /// Number of bodies tracked
        /// </summary>
        private int bodyCount;

        /// <summary>
        /// Face frame sources
        /// </summary>
        private FaceFrameSource[] faceFrameSources = null;

        /// <summary>
        /// Face frame readers
        /// </summary>
        private FaceFrameReader[] faceFrameReaders = null;

        /// <summary>
        /// Storage for face frame results
        /// </summary>
        private FaceFrameResult[] faceFrameResults = null;

        private Rect? LastFaceLocation = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap bitmap = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap infBitmap = null;

        /// <summary>
        /// Buffer size for bitmap
        /// </summary>
        public uint bitmapBackBufferSize = 0;

        private DepthFrameReader depthFrameReader = null;
        private FrameDescription depthFrameDescription = null;

        private bool StableFace = false;

        private Queue<Rect> LastFaces = new Queue<Rect>();

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// From MS Sample Project
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        private bool ShowDay = true;

        public MainWindow()
        {
            // Get the current Kinect for the system
            this.kinectSensor = KinectSensor.GetDefault();

            // Initialize the Multi-Source Frame Reader
            // The following source types are available:
            //      -Audio (Goal is to have audio, but I'm not sure if it needs to be part of this multi reader - I know the audio doesn't work at the same (frame) rate as some other sources
            //      -Body  (Not needed?)
            //      -BodyIndex (Not needed?)
            //      -Color (Used for primary display, and possibly analysis)
            //      -Depth (Used for analysis - Respiratory? Distance for Temperature?)
            //      -Infrared (Used for Heart Rate? Temperature?)
            //      -Long Exposure Infrared (Used for Heart Rate? Temperature? - More research is needed)
            //this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.Infrared);

            //this.multiSourceFrameReader.MultiSourceFrameArrived += multiSourceFrameReader_MultiSourceFrameArrived;

            this.infraredFrameReader = this.kinectSensor.InfraredFrameSource.OpenReader();

            this.infraredFrameReader.FrameArrived += infraredFrameReader_FrameArrived;

            this.colorFrameReader = this.kinectSensor.ColorFrameSource.OpenReader();

            this.colorFrameReader.FrameArrived += colorFrameReader_FrameArrived;

            this.bodyFrameReader = this.kinectSensor.BodyFrameSource.OpenReader();

            this.bodyFrameReader.FrameArrived += bodyFrameReader_FrameArrived;

            this.depthFrameReader = this.kinectSensor.DepthFrameSource.OpenReader();

            this.depthFrameReader.FrameArrived += depthFrameReader_FrameArrived;

            // set the maximum number of bodies that would be tracked by Kinect
            this.bodyCount = this.kinectSensor.BodyFrameSource.BodyCount;

            // allocate storage to store body objects
            this.bodies = new Body[this.bodyCount];

            // specify the required face frame results
            FaceFrameFeatures faceFrameFeatures =
                FaceFrameFeatures.BoundingBoxInColorSpace
                | FaceFrameFeatures.PointsInColorSpace
                | FaceFrameFeatures.RotationOrientation
                | FaceFrameFeatures.FaceEngagement
                | FaceFrameFeatures.Glasses
                | FaceFrameFeatures.Happy
                | FaceFrameFeatures.LeftEyeClosed
                | FaceFrameFeatures.RightEyeClosed
                | FaceFrameFeatures.LookingAway
                | FaceFrameFeatures.MouthMoved
                | FaceFrameFeatures.MouthOpen;

            // create a face frame source + reader to track each face in the FOV
            this.faceFrameSources = new FaceFrameSource[this.bodyCount];
            this.faceFrameReaders = new FaceFrameReader[this.bodyCount];
            for (int i = 0; i < this.bodyCount; i++)
            {
                // create the face frame source with the required face frame features and an initial tracking Id of 0
                this.faceFrameSources[i] = new FaceFrameSource(this.kinectSensor, 0, faceFrameFeatures);

                // open the corresponding reader
                this.faceFrameReaders[i] = this.faceFrameSources[i].OpenReader();
            }

            // allocate storage to store face frame results for each face in the FOV
            this.faceFrameResults = new FaceFrameResult[this.bodyCount];

            // Set up coordinate mapper
            //this.coordinateMapper = this.kinectSensor.CoordinateMapper;
            

            // Get the color Frame info, and create a bitmap of that size
            FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
            int colorWidth = colorFrameDescription.Width;
            int colorHeight = colorFrameDescription.Height;
            this.bitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Bgra32, null);
            this.bitmapBackBufferSize = (uint)((this.bitmap.BackBufferStride * (this.bitmap.PixelHeight - 1)) + (this.bitmap.PixelWidth * this.bytesPerPixel));

            this.infFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;
            this.infBitmap = new WriteableBitmap(this.infFrameDescription.Width, this.infFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray32Float, null);
            this.depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;

            // Basic connection and methods to convey connection state
            this.kinectSensor.IsAvailableChanged += kinectSensor_IsAvailableChanged;

            this.Closing += MainWindow_Closing;

            // Attempt to Open the Kinect Sensor
            this.kinectSensor.Open();

            // Place-holder - just fire the changed event to show the initial state
            //kinectSensor_IsAvailableChanged(this, null);

            this.DataContext = this;

            this.InitializeComponent();
        }

        // Checks for total depth in an effort to detect respiratory rate
        // This seems to use up all available resources
        // Should be optimized and/or greatly reduce the range to check
        void depthFrameReader_FrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            return; // Skip - see note below
            unsafe
            {
                using (DepthFrame depthFrame = e.FrameReference.AcquireFrame())
                {
                    if (depthFrame != null)
                    {
                        using (Microsoft.Kinect.KinectBuffer depthBuffer = depthFrame.LockImageBuffer())
                        {
                            ushort* frameData = (ushort*)depthBuffer.UnderlyingBuffer;
                            double TotalVolume = 0;
                            ushort minValue = depthFrame.DepthMinReliableDistance;
                            ushort maxValue = ushort.MaxValue;
                            ushort depthAdjust = 8000 / 256;

                            // NOTE: This is too slow
                            // I have not analyzed why - it doesn't seem to me like it should be that slow
                            // But this event triggering will block out all other processing
                            // Open to suggestions
                            // It only iterates over ~200k points 30x per second

                            // Original Iteration
                            //for (int i = 0; i < (int)depthBuffer.Size / this.depthFrameDescription.BytesPerPixel; i++)

                            // Limited Sampling - This keeps up on my machine

                            // I beleive that getting the depthFrameDescription's BytesPerPixel may have been the bottleneck?
                            int testSampleRate = 1;
                            for (int i = 0; i < (int)depthBuffer.Size / (testSampleRate *2); i++)
                            {
                                ushort depth = frameData[i * testSampleRate];
                                TotalVolume += (depth >= minValue && depth <= maxValue ? (depth / depthAdjust) : 0);
                            }
                        }
                    }
                }
            }
        }

        void bodyFrameReader_FrameArrived(object sender, BodyFrameArrivedEventArgs e)
        {
            LastFaceLocation = null;
            using (var bodyFrame = e.FrameReference.AcquireFrame())
            {
                if (bodyFrame != null)
                {
                    bodyFrame.GetAndRefreshBodyData(this.bodies);

                    for (int i = 0; i < this.bodyCount; i++)
                    {
                        if (this.faceFrameSources[i].IsTrackingIdValid)
                        {
                            if (this.faceFrameResults[i] != null)
                            {
                                if (ShowDay)
                                {
                                    var box = faceFrameResults[i].FaceBoundingBoxInColorSpace;
                                    LastFaceLocation = new Rect(new Point(box.Left, box.Bottom), new Point(box.Right, box.Top));

                                    LastFaces.Enqueue((Rect)LastFaceLocation);
                                    // Experiment in monitoring stability
                                    // Requires faces found in 30 straight frames
                                    // Where the most recent is within 10 pixels of the running averages
                                    // I think this is still too volatile, but it could be tuned.
                                    // I think the face tracking itself may be contributing to the volatility as much as actual movement
                                    if ((LastFaces.Count > 30) && (LastFaceLocation.Value.Bottom - LastFaces.Average(m => m.Bottom) < 10) && (LastFaceLocation.Value.Top - LastFaces.Average(m => m.Top) < 10) && (LastFaceLocation.Value.Left - LastFaces.Average(m => m.Left) < 10) && (LastFaceLocation.Value.Right - LastFaces.Average(m => m.Right) < 10))
                                    {
                                        LastFaces.Dequeue();
                                        StableFace = true;
                                        Console.WriteLine("Stable");
                                    }
                                    else
                                    {
                                        StableFace = false;
                                        Console.WriteLine("Drawing Color face: " + LastFaceLocation.ToString());
                                    }
                                }
                                else
                                {
                                    var box = faceFrameResults[i].FaceBoundingBoxInInfraredSpace;
                                    LastFaceLocation = new Rect(new Point(box.Left, box.Bottom), new Point(box.Right, box.Top));
                                    Console.WriteLine("Drawing Infrared face: " + LastFaceLocation.ToString());
                                    lblBPM.Content = "BPM: 60?";
                                    lblRR.Content = "Resp: 20?";
                                    lblTemp.Content = "Temp: 98?";
                                    return;
                                }
                            }
                            else
                            {
                                StableFace = false;
                                LastFaces.Clear();
                            }
                        }
                        else
                        {
                            // check if the corresponding body is tracked 
                            if (this.bodies[i].IsTracked)
                            {
                                // update the face frame source to track this body
                                this.faceFrameSources[i].TrackingId = this.bodies[i].TrackingId;
                            }
                        }
                    }
                }
            }

            if (StableFace)
            {
                lblBPM.Content = "BPM: 60?";
                lblRR.Content = "Resp: 20?";
                lblTemp.Content = "Temp: 98?";
            }
            else {
                lblBPM.Content = "BPM: N/A";
                lblRR.Content = "Resp: N/A";
                lblTemp.Content = "Temp: N/A";
            }

        }

        void colorFrameReader_FrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            // ColorFrame is IDisposable
            using (ColorFrame colorFrame = e.FrameReference.AcquireFrame())
            {
                if (colorFrame != null)
                {
                    FrameDescription colorFrameDescription = colorFrame.FrameDescription;

                    using (KinectBuffer colorBuffer = colorFrame.LockRawImageBuffer())
                    {
                        this.bitmap.Lock();

                        // verify data and write the new color frame data to the display bitmap
                        if ((colorFrameDescription.Width == this.bitmap.PixelWidth) && (colorFrameDescription.Height == this.bitmap.PixelHeight))
                        {
                            colorFrame.CopyConvertedFrameDataToIntPtr(
                                this.bitmap.BackBuffer,
                                (uint)(colorFrameDescription.Width * colorFrameDescription.Height * 4),
                                ColorImageFormat.Bgra);

                            this.bitmap.AddDirtyRect(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight));
                        }

                        this.bitmap.Unlock();
                    }
                }
            }
        }

        void infraredFrameReader_FrameArrived(object sender, InfraredFrameArrivedEventArgs e)
        {
            // InfraredFrame is IDisposable
            using (InfraredFrame infraredFrame = e.FrameReference.AcquireFrame())
            {
                if (infraredFrame != null)
                {
                    // the fastest way to process the infrared frame data is to directly access 
                    // the underlying buffer
                    using (Microsoft.Kinect.KinectBuffer infraredBuffer = infraredFrame.LockImageBuffer())
                    {
                        // verify data and write the new infrared frame data to the display bitmap
                        if (((this.infFrameDescription.Width * this.infFrameDescription.Height) == (infraredBuffer.Size / this.infFrameDescription.BytesPerPixel)) &&
                            (this.infFrameDescription.Width == this.infBitmap.PixelWidth) && (this.infFrameDescription.Height == this.infBitmap.PixelHeight))
                        {
                            this.ProcessInfraredFrameData(infraredBuffer.UnderlyingBuffer, infraredBuffer.Size);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles all data received
        /// Displays selected data, analyzes metrics, etc
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void multiSourceFrameReader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            // CURRENTLY DISABLED
            Console.WriteLine("MSF!");
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();
            bool isBitmapLocked = false;

            if (multiSourceFrame == null)
            {
                Console.WriteLine("MSF is null");
                return;
            }

            try
            {

                ColorFrame colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame();
                if (colorFrame != null)
                {
                    this.bitmap.Lock();
                    isBitmapLocked = true;
                    colorFrame.CopyConvertedFrameDataToIntPtr(this.bitmap.BackBuffer, this.bitmapBackBufferSize, ColorImageFormat.Bgra);
                    colorFrame.Dispose();
                    colorFrame = null;
                    if (LastFaceLocation == null)
                    {
                        this.bitmap.AddDirtyRect(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight));
                    }
                    else
                    {
                        this.bitmap.AddDirtyRect(new Int32Rect((int)LastFaceLocation.Value.BottomLeft.X, (int)LastFaceLocation.Value.BottomLeft.Y, (int)LastFaceLocation.Value.Width, (int)LastFaceLocation.Value.Height));
                    }
                }
                else
                {
                    Console.WriteLine("CF is null");
                }

                InfraredFrame infFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame();
                if (infFrame != null)
                {
                    // the fastest way to process the infrared frame data is to directly access 
                    // the underlying buffer
                    using (Microsoft.Kinect.KinectBuffer infraredBuffer = infFrame.LockImageBuffer())
                    {
                        // verify data and write the new infrared frame data to the display bitmap
                        if (((this.infFrameDescription.Width * this.infFrameDescription.Height) == (infraredBuffer.Size / this.infFrameDescription.BytesPerPixel)) &&
                            (this.infFrameDescription.Width == this.infBitmap.PixelWidth) && (this.infFrameDescription.Height == this.infBitmap.PixelHeight))
                        {
                            this.ProcessInfraredFrameData(infraredBuffer.UnderlyingBuffer, infraredBuffer.Size);
                        }
                    }

                }
                else
                {
                    Console.WriteLine("IF is null");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MSF Arrived Error: " + ex.ToString());
            }
            finally
            {
                if (isBitmapLocked)
                {
                    this.bitmap.Unlock();
                }
            }
        }

        private unsafe void ProcessInfraredFrameData(IntPtr infraredFrameData, uint infraredFrameDataSize)
        {
            // infrared frame data is a 16 bit value
            ushort* frameData = (ushort*)infraredFrameData;

            // lock the target bitmap
            this.infBitmap.Lock();

            // get the pointer to the bitmap's back buffer
            float* backBuffer = (float*)this.infBitmap.BackBuffer;

            // process the infrared data
            for (int i = 0; i < (int)(infraredFrameDataSize / this.infFrameDescription.BytesPerPixel); ++i)
            {
                // since we are displaying the image as a normalized grey scale image, we need to convert from
                // the ushort data (as provided by the InfraredFrame) to a value from [InfraredOutputValueMinimum, InfraredOutputValueMaximum]
                backBuffer[i] = Math.Min(InfraredOutputValueMaximum, (((float)frameData[i] / InfraredSourceValueMaximum * InfraredSourceScale) * (1.0f - InfraredOutputValueMinimum)) + InfraredOutputValueMinimum);
            }

            // mark the entire bitmap as needing to be drawn
            this.infBitmap.AddDirtyRect(new Int32Rect(0, 0, this.infBitmap.PixelWidth, this.infBitmap.PixelHeight));

            // unlock the bitmap
            this.infBitmap.Unlock();
        }

        public ImageSource ImageSource
        {
            get
            {
                return this.bitmap;
            }
        }

        public ImageSource NightSource
        {
            get
            {
                return this.infBitmap;
            }
        }


        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            //if (this.multiSourceFrameReader != null)
            //{
            //    // MultiSourceFrameReder is IDisposable
            //    this.multiSourceFrameReader.Dispose();
            //    this.multiSourceFrameReader = null;
            //}

            if (this.bodyFrameReader != null)
            {
                this.bodyFrameReader.Dispose();
                this.bodyFrameReader = null;
            }

            if (this.colorFrameReader != null)
            {
                this.colorFrameReader.Dispose();
                this.colorFrameReader = null;
            }

            if (this.depthFrameReader != null)
            {
                this.depthFrameReader.Dispose();
                this.depthFrameReader = null;
            }

            for (int i = 0; i < this.bodyCount; i++)
            {
                if (this.faceFrameReaders[i] != null)
                {
                    // FaceFrameReader is IDisposable
                    this.faceFrameReaders[i].Dispose();
                    this.faceFrameReaders[i] = null;
                }

                if (this.faceFrameSources[i] != null)
                {
                    // FaceFrameSource is IDisposable
                    this.faceFrameSources[i].Dispose();
                    this.faceFrameSources[i] = null;
                }
            }

            if (this.infraredFrameReader != null)
            {
                this.infraredFrameReader.Dispose();
                this.infraredFrameReader = null;
            }

            if (this.kinectSensor != null)
            {
                this.kinectSensor.Close();
                this.kinectSensor = null;
            }
        }

        void kinectSensor_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            // TODO - Add notification to UI
            Console.WriteLine("Kinect Connected: " + this.kinectSensor.IsAvailable.ToString());
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ShowDay = true;
            DayPic.Visibility = Visibility.Visible;
            NightPic.Visibility = Visibility.Hidden;
            //this.bitmap = new WriteableBitmap(1920, 1080, 96.0, 96.0, PixelFormats.Bgra32, null);
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            ShowDay = false;
            DayPic.Visibility = Visibility.Hidden;
            NightPic.Visibility = Visibility.Visible;
            //this.bitmap = new WriteableBitmap(512, 424, 96.0, 96.0, PixelFormats.Bgra32, null);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < this.bodyCount; i++)
            {
                if (this.faceFrameReaders[i] != null)
                {
                    // wire handler for face frame arrival
                    this.faceFrameReaders[i].FrameArrived += MainWindow_FrameArrived;
                }
            }

            if (this.bodyFrameReader != null)
            {
                // wire handler for body frame arrival
                this.bodyFrameReader.FrameArrived += bodyFrameReader_FrameArrived;
            }
        }

        void MainWindow_FrameArrived(object sender, FaceFrameArrivedEventArgs e)
        {
            using (FaceFrame faceFrame = e.FrameReference.AcquireFrame())
            {
                if (faceFrame != null)
                {
                    // get the index of the face source from the face source array
                    int index = this.GetFaceSourceIndex(faceFrame.FaceFrameSource);

                    // check if this face frame has valid face frame results
                    if (this.ValidateFaceBoxAndPoints(faceFrame.FaceFrameResult))
                    {
                        // store this face frame result to draw later
                        this.faceFrameResults[index] = faceFrame.FaceFrameResult;
                    }
                    else
                    {
                        // indicates that the latest face frame result from this reader is invalid
                        this.faceFrameResults[index] = null;
                    }
                }
            }
        }

        private int GetFaceSourceIndex(FaceFrameSource faceFrameSource)
        {
            int index = -1;

            for (int i = 0; i < this.bodyCount; i++)
            {
                if (this.faceFrameSources[i] == faceFrameSource)
                {
                    index = i;
                    break;
                }
            }

            return index;
        }

        private bool ValidateFaceBoxAndPoints(FaceFrameResult faceResult)
        {
            bool isFaceValid = faceResult != null;

            if (isFaceValid)
            {
                var faceBox = faceResult.FaceBoundingBoxInColorSpace;
                if (faceBox != null)
                {
                    // check if we have a valid rectangle within the bounds of the screen space
                    isFaceValid = (faceBox.Right - faceBox.Left) > 0 &&
                                  (faceBox.Bottom - faceBox.Top) > 0 &&
                                  faceBox.Right <= 1980 &&
                                  faceBox.Bottom <= 1020;
                    // used to be this.displayWidth and this.displayHeight
                    if (isFaceValid)
                    {
                        var facePoints = faceResult.FacePointsInColorSpace;
                        if (facePoints != null)
                        {
                            foreach (PointF pointF in facePoints.Values)
                            {
                                // check if we have a valid face point within the bounds of the screen space
                                bool isFacePointValid = pointF.X > 0.0f &&
                                                        pointF.Y > 0.0f &&
                                                        pointF.X < 1980 &&
                                                        pointF.Y < 1020;

                                if (!isFacePointValid)
                                {
                                    isFaceValid = false;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return isFaceValid;
        }
    }
}
