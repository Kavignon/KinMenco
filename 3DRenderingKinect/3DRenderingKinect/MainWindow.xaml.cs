//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.CoordinateMappingBasics
{
    using Microsoft.Kinect;
    using System;
    using System.ComponentModel;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>
    /// Interaction logic for MainWindow
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        /// <summary>
        /// Size of the RGB pixel in the bitmap
        /// </summary>
        private readonly int _bytesPerPixel = (PixelFormats.Bgr32.BitsPerPixel + 7) / 8;

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor _kinectSensor;

        /// <summary>
        /// Map depth range to byte range
        /// </summary>
        private const int MapDepthToByte = 8000 / 256;

        /// <summary>
        /// Coordinate mapper to map one type of point to another
        /// </summary>
        private readonly CoordinateMapper coordinateMapper;

        /// <summary>
        /// Reader for depth/color/body index frames
        /// </summary>
        private MultiSourceFrameReader _multiFrameSourceReader;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private readonly WriteableBitmap _colorBitmap;

        /// <summary>
        /// Bitmap to display
        /// </summary>
        private readonly WriteableBitmap _depthBitmap;

        /// <summary>
        /// The size in bytes of the bitmap back buffer
        /// </summary>
        private readonly uint _bitmapBackBufferSize = 0;

        /// <summary>
        /// Intermediate storage for the color to depth mapping
        /// </summary>
        private readonly DepthSpacePoint[] _colorMappedToDepthPoints; //correspondance

        /// <summary>
        /// Current status text to display
        /// </summary>
        private string statusText = null;

        private byte[] mappedByteImage = new byte[1920 * 1080];

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ColorImageSource => _colorBitmap;

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource DepthImageSource => _depthBitmap;

        //Me faire un tableau de 1920 x 1080 tableau de bytes
        //(uint16 -> vouloir un tableau de byte (conversion de donnees 16bit -8bit)
        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            this._kinectSensor = KinectSensor.GetDefault();

            this._multiFrameSourceReader = this._kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Depth | FrameSourceTypes.Color);

            this._multiFrameSourceReader.MultiSourceFrameArrived += this.Reader_MultiSourceFrameArrived;

            this.coordinateMapper = this._kinectSensor.CoordinateMapper;

            FrameDescription depthFrameDescription = this._kinectSensor.DepthFrameSource.FrameDescription;

            int depthWidth = depthFrameDescription.Width;
            int depthHeight = depthFrameDescription.Height;

            FrameDescription colorFrameDescription = this._kinectSensor.ColorFrameSource.FrameDescription;

            int colorWidth = colorFrameDescription.Width;
            int colorHeight = colorFrameDescription.Height;

            this._colorMappedToDepthPoints = new DepthSpacePoint[colorWidth * colorHeight];

            this._colorBitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Bgra32, null);

            this._depthBitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Bgra32, null);

            // Calculate the WriteableBitmap back buffer size
            this._bitmapBackBufferSize = (uint)((this._colorBitmap.BackBufferStride * (this._colorBitmap.PixelHeight - 1)) + (this._colorBitmap.PixelWidth * this._bytesPerPixel));

            this._kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            this._kinectSensor.Open();

            this.StatusText = this._kinectSensor.IsAvailable ? Properties.Resources.RunningStatusText
                                                            : Properties.Resources.NoSensorStatusText;

            this.DataContext = this;

            this.InitializeComponent();
        }

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets or sets the current status text to display
        /// </summary>
        public string StatusText
        {
            get
            {
                return this.statusText;
            }

            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;

                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        public byte[] MappedByteImage
        {
            get
            {
                return mappedByteImage;
            }

            set
            {
                mappedByteImage = value;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (this._multiFrameSourceReader != null)
            {
                // MultiSourceFrameReder is IDisposable
                this._multiFrameSourceReader.Dispose();
                this._multiFrameSourceReader = null;
            }

            if (this._kinectSensor != null)
            {
                this._kinectSensor.Close();
                this._kinectSensor = null;
            }
        }

        /// <summary>
        /// Handles the depth/color/body index frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            int depthWidth = 0;
            int depthHeight = 0;

            DepthFrame depthFrame = null;
            ColorFrame colorFrame = null;
            bool isBitmapLocked = false;

            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            // If the Frame has expired by the time we process this event, return.
            if (multiSourceFrame == null)
            {
                return;
            }

            // We use a try/finally to ensure that we clean up before we exit the function.
            // This includes calling Dispose on any Frame objects that we may have and unlocking the bitmap back buffer.
            try
            {
                depthFrame = multiSourceFrame.DepthFrameReference.AcquireFrame();
                colorFrame = multiSourceFrame.ColorFrameReference.AcquireFrame();

                // If any frame has expired by the time we process this event, return.
                // The "finally" statement will Dispose any that are not null.
                if ((depthFrame == null) || (colorFrame == null))
                {
                    return;
                }

                // Process Depth
                FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                depthWidth = depthFrameDescription.Width;
                depthHeight = depthFrameDescription.Height;

                ushort[] frameData = new ushort[512 * 424];
                // Access the depth frame data directly via LockImageBuffer to avoid making a copy
                using (KinectBuffer depthFrameData = depthFrame.LockImageBuffer())
                {
                    this.coordinateMapper.MapColorFrameToDepthSpaceUsingIntPtr(
                        depthFrameData.UnderlyingBuffer,
                        depthFrameData.Size,
                        this._colorMappedToDepthPoints);
                }

                depthFrame.CopyFrameDataToArray(frameData);
                ushort minDepth = depthFrame.DepthMinReliableDistance;



                // Process Color

                // Lock the bitmap for writing
                this._colorBitmap.Lock();
                _depthBitmap.Lock();
                isBitmapLocked = true;

                colorFrame.CopyConvertedFrameDataToIntPtr(this._colorBitmap.BackBuffer, this._bitmapBackBufferSize,
                    ColorImageFormat.Bgra);

                // We're done with the ColorFrame
                colorFrame.Dispose();
                colorFrame = null;

                unsafe
                {
                    int colorMappedToDepthPointCount = this._colorMappedToDepthPoints.Length;

                    fixed (DepthSpacePoint* colorMappedToDepthPointsPointer = this._colorMappedToDepthPoints)
                    {
                        // Treat the color data as 4-byte pixels
                        uint* bitmapPixelsPointer = (uint*)this._colorBitmap.BackBuffer; 

                        // Loop over each row and column of the color image
                        // Zero out any pixels that don't correspond to a body index
                        for (int colorIndex = 0; colorIndex < colorMappedToDepthPointCount; ++colorIndex)
                        {
                            float colorMappedToDepthX = colorMappedToDepthPointsPointer[colorIndex].X;
                            float colorMappedToDepthY = colorMappedToDepthPointsPointer[colorIndex].Y;

                            // The sentinel value is -inf, -inf, meaning that no depth pixel corresponds to this color pixel.
                            if (!float.IsNegativeInfinity(colorMappedToDepthX) &&
                                !float.IsNegativeInfinity(colorMappedToDepthY))
                            {
                                // Make sure the depth pixel maps to a valid point in color space
                                int depthX = (int)(colorMappedToDepthX + 0.5f);
                                int depthY = (int)(colorMappedToDepthY + 0.5f);

                                // If the point is not valid, there is no body index there.
                                if ((depthX >= 0) && (depthX < depthWidth) && (depthY >= 0) && (depthY < depthHeight))
                                {
                                    int depthIndex = (depthY * depthWidth) + depthX;
                                    var depth = frameData[depthIndex];
                                    var maxDepth = ushort.MaxValue;
                                    MappedByteImage[colorIndex] = (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);
                                }
                            }
                        }
                    }
                    this._colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this._colorBitmap.PixelWidth, this._colorBitmap.PixelHeight));
                    this._depthBitmap.AddDirtyRect(new Int32Rect(0, 0, 512, 424));

                }
            }
            finally
            {
                if (isBitmapLocked)
                {
                    this._colorBitmap.Unlock();
                    _depthBitmap.Unlock();
                }

                depthFrame?.Dispose();

                colorFrame?.Dispose();
            }
        }

        public byte ConvertInt16ToByte(Int16 colorValue) => (byte)Math.Round((colorValue / 65535.0) * 255);

        public void ProcessColorImage(ColorFrame colorFrame, ushort minDepth, int depthWidth, int depthHeight,
            ushort[] frameData)
        {
            // Process Color

            // Lock the bitmap for writing
            this._colorBitmap.Lock();
            var isBitmapLocked = true;

            colorFrame.CopyConvertedFrameDataToIntPtr(this._colorBitmap.BackBuffer, this._bitmapBackBufferSize,
                ColorImageFormat.Bgra);

            // We're done with the ColorFrame
            colorFrame.Dispose();
            colorFrame = null;

            unsafe
            {
                int colorMappedToDepthPointCount = this._colorMappedToDepthPoints.Length;

                fixed (DepthSpacePoint* colorMappedToDepthPointsPointer = this._colorMappedToDepthPoints)
                {
                    // Treat the color data as 4-byte pixels
                    uint* bitmapPixelsPointer = (uint*)this._colorBitmap.BackBuffer;

                    // Loop over each row and column of the color image
                    // Zero out any pixels that don't correspond to a body index
                    for (int colorIndex = 0; colorIndex < colorMappedToDepthPointCount; ++colorIndex)
                    {
                        float colorMappedToDepthX = colorMappedToDepthPointsPointer[colorIndex].X;
                        float colorMappedToDepthY = colorMappedToDepthPointsPointer[colorIndex].Y;

                        // The sentinel value is -inf, -inf, meaning that no depth pixel corresponds to this color pixel.
                        if (!float.IsNegativeInfinity(colorMappedToDepthX) &&
                            !float.IsNegativeInfinity(colorMappedToDepthY))
                        {
                            // Make sure the depth pixel maps to a valid point in color space
                            int depthX = (int)(colorMappedToDepthX + 0.5f);
                            int depthY = (int)(colorMappedToDepthY + 0.5f);

                            // If the point is not valid, there is no body index there.
                            if ((depthX >= 0) && (depthX < depthWidth) && (depthY >= 0) && (depthY < depthHeight))
                            {
                                int depthIndex = (depthY * depthWidth) + depthX;
                                var depth = frameData[depthIndex];
                                var maxDepth = ushort.MaxValue;
                                MappedByteImage[colorIndex] =
                                    (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);
                            }
                        }
                    }
                }
                _colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this._colorBitmap.PixelWidth, this._colorBitmap.PixelHeight));
            }
        }


        //    public void ProcessDepthImage(DepthFrame depthFrame)
        //    {
        //        bool depthFrameProcessed = false;

        //        if (depthFrame == null)
        //        {
        //            return;
        //        }

        //        using (var depthBuffer = depthFrame.LockImageBuffer())
        //        {
        //            // Process Depth
        //            FrameDescription depthFrameDescription = depthFrame.FrameDescription;

        //           var depthWidth = depthFrameDescription.Width;
        //           var depthHeight = depthFrameDescription.Height;

        //            ushort[] frameData = new ushort[512 * 424];
        //            // Access the depth frame data directly via LockImageBuffer to avoid making a copy
        //            using (KinectBuffer depthFrameData = depthFrame.LockImageBuffer())
        //            {
        //                this.coordinateMapper.MapColorFrameToDepthSpaceUsingIntPtr(
        //                    depthFrameData.UnderlyingBuffer,
        //                    depthFrameData.Size,
        //                    this._colorMappedToDepthPoints);
        //            }

        //            depthFrame.CopyFrameDataToArray(frameData);
        //            ushort maxDepth = ushort.MaxValue;

        //            this.ProcessDepthFrameData(depthBuffer.UnderlyingBuffer, depthBuffer.Size, depthFrame.DepthMinReliableDistance, maxDepth);
        //            depthFrameProcessed = true;

        //            if (depthFrameProcessed)
        //            {
        //                this.RenderDepthPixels();    
        //            }
        //        }


        //            }
        //        }
        //            }



        //    }

        //}


    }
}