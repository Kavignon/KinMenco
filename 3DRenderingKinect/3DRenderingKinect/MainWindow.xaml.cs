//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace _3DRenderingKinect
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
        private readonly CoordinateMapper _coordinateMapper;

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
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource ColorImageSource => _colorBitmap;

        /// <summary>
        /// Gets the bitmap to display
        /// </summary>
        public ImageSource DepthImageSource => _depthBitmap;

        private readonly BitmapSource _headerBitmap;

        private byte[] _rawPixelData;

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

            this._coordinateMapper = this._kinectSensor.CoordinateMapper;

            FrameDescription depthFrameDescription = this._kinectSensor.DepthFrameSource.FrameDescription;

            int depthWidth = depthFrameDescription.Width;
            int depthHeight = depthFrameDescription.Height;

            FrameDescription colorFrameDescription = this._kinectSensor.ColorFrameSource.FrameDescription;

            int colorWidth = colorFrameDescription.Width;
            int colorHeight = colorFrameDescription.Height;

            this._colorMappedToDepthPoints = new DepthSpacePoint[colorWidth * colorHeight];

            this._colorBitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Bgra32, null);

            this._depthBitmap = new WriteableBitmap(colorWidth, colorHeight, 96.0, 96.0, PixelFormats.Gray8, null);

            // Calculate the WriteableBitmap back buffer size
            this._bitmapBackBufferSize = (uint)((this._colorBitmap.BackBufferStride * (this._colorBitmap.PixelHeight - 1)) + (this._colorBitmap.PixelWidth * this._bytesPerPixel));

            this._kinectSensor.Open();

            var bitmapUri = new Uri("pack://application:,,,/Images/binary.bmp"); //path absolu vers la ressource de l'image
            _headerBitmap = new BitmapImage(bitmapUri);

            this.DataContext = this;
            this.InitializeComponent();
        }

        private void RenderHeaderLeftCorner()
        {
            ReadImagePixelBuffer();
            HeaderColorBuffering();
        }

        /// <summary>
        /// INotifyPropertyChangedPropertyChanged event to allow window controls to bind to changeable data
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        public byte[] MappedByteImage { get; set; } = new byte[1920 * 1080];

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

            if (this._kinectSensor == null) return;

            this._kinectSensor.Close();
            this._kinectSensor = null;
        }

        private void ReadImagePixelBuffer()
        {
            _rawPixelData = new byte[_headerBitmap.PixelWidth * 4 * _headerBitmap.PixelHeight]; //Raw header image (buffer)
            _headerBitmap.CopyPixels(_rawPixelData, _headerBitmap.PixelWidth * 4, 0);
        }

        private void HeaderColorBuffering()
        {
            var region = new Int32Rect(0, 0, _headerBitmap.PixelWidth, _headerBitmap.PixelHeight);
            var imageRenderBuffer = new byte[_headerBitmap.PixelWidth * 4 * _headerBitmap.PixelHeight];
            _colorBitmap.CopyPixels(region, imageRenderBuffer, _headerBitmap.PixelWidth * 4, 0);
            FinalizeOutputBuffering(region, imageRenderBuffer);
        }

        private void FinalizeOutputBuffering(Int32Rect regionRect, byte[] colorBuffer)
        {
            var outputBuffer = new byte[_headerBitmap.PixelWidth * 4 * _headerBitmap.PixelHeight];
            for (var i = 0; i < colorBuffer.Length; i += 4)
            {
                outputBuffer[i] = _rawPixelData[i]; //Blue

                outputBuffer[i + 1] = colorBuffer[i + 1];
                outputBuffer[i + 2] = colorBuffer[i + 2];
                outputBuffer[i + 3] = colorBuffer[i + 3];
            }

            _colorBitmap.Lock();
            _colorBitmap.WritePixels(regionRect, outputBuffer, _headerBitmap.PixelWidth * 4, 0);
            _colorBitmap.AddDirtyRect(regionRect);
            _colorBitmap.Unlock();
        }

        /// <summary>
        /// Handles the depth/color/body index frame data arriving from the sensor
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void Reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            DepthFrame depthFrame = null;
            ColorFrame colorFrame = null;
            var multiSourceFrame = e.FrameReference.AcquireFrame();

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
                var mappedByteImage = new byte[1920 * 1080];
                // If any frame has expired by the time we process this event, return.
                // The "finally" statement will Dispose any that are not null.
                if ((depthFrame == null) || (colorFrame == null))
                {
                    return;
                }

                // Process Depth
                _depthBitmap.Lock();
                FrameDescription depthFrameDescription = depthFrame.FrameDescription;

                var depthWidth = depthFrameDescription.Width;
                var depthHeight = depthFrameDescription.Height;

                var frameData = new ushort[512 * 424];

                // Access the depth frame data directly via LockImageBuffer to avoid making a copy
                using (KinectBuffer depthFrameData = depthFrame.LockImageBuffer())
                {
                    this._coordinateMapper.MapColorFrameToDepthSpaceUsingIntPtr(
                        depthFrameData.UnderlyingBuffer,
                        depthFrameData.Size,
                        this._colorMappedToDepthPoints);
                }

                depthFrame.CopyFrameDataToArray(frameData);
                ushort minDepth = depthFrame.DepthMinReliableDistance;

                ProcessColorImage(colorFrame);
                RenderHeaderLeftCorner();
                ProcessDepthImage(depthWidth, depthHeight, frameData, mappedByteImage, minDepth);
            }
            finally
            {
                if(depthFrame != null)
                    depthFrame.Dispose();
                if(colorFrame != null)
                    colorFrame.Dispose();
            }
        }

        private unsafe void ProcessDepthImage(int depthWidth, int depthHeight, ushort[] frameData, byte[] mappedByteImage,
            ushort minDepth)
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
                            mappedByteImage[colorIndex] =
                                (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);
                        }
                    }
                }
            }

            _depthBitmap.WritePixels(new Int32Rect(0, 0, 1920, 1080),  mappedByteImage, _depthBitmap.PixelWidth, 0);
            _depthBitmap.Unlock();
        }

        private void ProcessColorImage(ColorFrame colorFrame)
        {
            // Process Color
            // Lock the bitmap for writing
            this._colorBitmap.Lock();
            colorFrame.CopyConvertedFrameDataToIntPtr(this._colorBitmap.BackBuffer, this._bitmapBackBufferSize,
                ColorImageFormat.Bgra);
            this._colorBitmap.AddDirtyRect(new Int32Rect(0, 0, this._colorBitmap.PixelWidth, this._colorBitmap.PixelHeight));
            this._colorBitmap.Unlock();
        }
    }
}