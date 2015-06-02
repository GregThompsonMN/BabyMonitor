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

        /// <summary>
        /// Coordinate Mapper for sychnorizing data from different sources or in different coordinate systems
        /// </summary>
        private CoordinateMapper coordinateMapper = null;

        /// <summary>
        /// The reader for all data arriving from the Kinect
        /// My initial goal will be to include all data I think may at some point be useful
        /// </summary>
        private MultiSourceFrameReader multiSourceFrameReader = null;

        private FrameDescription infFrameDescription = null;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private WriteableBitmap bitmap = null;

        /// <summary>
        /// Buffer size for bitmap
        /// </summary>
        public uint bitmapBackBufferSize = 0;

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
            this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.Infrared);

            this.multiSourceFrameReader.MultiSourceFrameArrived += multiSourceFrameReader_MultiSourceFrameArrived;

            // Set up coordinate mapper
            this.coordinateMapper = this.kinectSensor.CoordinateMapper;

            // Get the color Frame info, and create a bitmap of that size
            FrameDescription colorFrameDescription = this.kinectSensor.ColorFrameSource.FrameDescription;
            int colorWidth = colorFrameDescription.Width;
            int colorHeight = colorFrameDescription.Height;
            this.bitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Bgra32, null);
            this.bitmapBackBufferSize = (uint)((this.bitmap.BackBufferStride * (this.bitmap.PixelHeight - 1)) + (this.bitmap.PixelWidth * this.bytesPerPixel));

            this.infFrameDescription = this.kinectSensor.InfraredFrameSource.FrameDescription;

            // Basic connection and methods to convey connection state
            this.kinectSensor.IsAvailableChanged += kinectSensor_IsAvailableChanged;

            this.Closing += MainWindow_Closing;

            // Attempt to Open the Kinect Sensor
            this.kinectSensor.Open();

            // Place-holder - just fire the changed event to show the initial state
            kinectSensor_IsAvailableChanged(this, null);

            this.DataContext = this;

            this.InitializeComponent();
        }

        /// <summary>
        /// Handles all data received
        /// Displays selected data, analyzes metrics, etc
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void multiSourceFrameReader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();
            bool isBitmapLocked = false;

            if (multiSourceFrame == null)
            {
                return;
            }

            try
            {
                if (ShowDay)
                {
                    ColorFrame colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame();

                    this.bitmap.Lock();
                    isBitmapLocked = true;
                    colorFrame.CopyConvertedFrameDataToIntPtr(this.bitmap.BackBuffer, this.bitmapBackBufferSize, ColorImageFormat.Bgra);
                    colorFrame.Dispose();
                    colorFrame = null;
                    this.bitmap.AddDirtyRect(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight));
                }
                else
                {
                    InfraredFrame infFrame = multiSourceFrame.InfraredFrameReference.AcquireFrame();

                    if (infFrame != null)
                    {
                        // the fastest way to process the infrared frame data is to directly access 
                        // the underlying buffer
                        using (Microsoft.Kinect.KinectBuffer infraredBuffer = infFrame.LockImageBuffer())
                        {
                            // verify data and write the new infrared frame data to the display bitmap
                            if (((this.infFrameDescription.Width * this.infFrameDescription.Height) == (infraredBuffer.Size / this.infFrameDescription.BytesPerPixel)) &&
                                (this.infFrameDescription.Width == this.bitmap.PixelWidth) && (this.infFrameDescription.Height == this.bitmap.PixelHeight))
                            {
                                this.ProcessInfraredFrameData(infraredBuffer.UnderlyingBuffer, infraredBuffer.Size);
                            }
                        }
                    }
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
            this.bitmap.Lock();

            // get the pointer to the bitmap's back buffer
            float* backBuffer = (float*)this.bitmap.BackBuffer;

            // process the infrared data
            for (int i = 0; i < (int)(infraredFrameDataSize / this.infFrameDescription.BytesPerPixel); ++i)
            {
                // since we are displaying the image as a normalized grey scale image, we need to convert from
                // the ushort data (as provided by the InfraredFrame) to a value from [InfraredOutputValueMinimum, InfraredOutputValueMaximum]
                backBuffer[i] = Math.Min(1.0f, (((float)frameData[i] / (float)ushort.MaxValue * 0.75f) * (1.0f - 0.01f)) + 0.01f);
            }

            // mark the entire bitmap as needing to be drawn
            this.bitmap.AddDirtyRect(new Int32Rect(0, 0, this.bitmap.PixelWidth, this.bitmap.PixelHeight));

            // unlock the bitmap
            this.bitmap.Unlock();
        }

        public ImageSource ImageSource
        {
            get
            {
                return this.bitmap;
            }
        }
        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this.multiSourceFrameReader != null)
            {
                // MultiSourceFrameReder is IDisposable
                this.multiSourceFrameReader.Dispose();
                this.multiSourceFrameReader = null;
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
            this.bitmap = new WriteableBitmap(1920, 1080, 96.0, 96.0, PixelFormats.Bgra32, null);
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            ShowDay = false;
            this.bitmap = new WriteableBitmap(512, 424, 96.0, 96.0, PixelFormats.Bgra32, null);
        }

    }
}
