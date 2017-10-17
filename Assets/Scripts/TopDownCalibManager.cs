using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using UnityEngine;
using System.Text;
using UnityEngine.VR.WSA;
using HoloLensCameraStream;
using SimpleJSON;
using System.Runtime.InteropServices;
#if NETFX_CORE
using Windows.Storage.Streams;
using Windows.Networking.Sockets;
using System.Threading.Tasks;
#else
using System.Net.Sockets;
#endif

// based on tutorial at https://github.com/WorldOfZero/Data-Sphere/blob/master/Assets/DataSphere/Scripts/Stream/NamedServerStream.cs
public class TopDownCalibManager : MonoBehaviour {



    private string TEST_RECEIVED_MSG_FROM_TOPDOWN = "{\"cx\": 328.90630324279033, \"cy\": 237.78931042000124, \"fx\": 514.24881005892848, \"fy\": 507.79248508954851, \"height\": 480, \"k1\": 0.0914051948147546, \"k2\": -0.22685463029938513, \"k3\": 0.18341275494159623, \"p1\": -0.022599626729701242, \"p2\": 0.002208592710481527, \"rms\": 0.39623399395854214, \"rvec\": [-0.56421140493062671, 0.13848225878287804, 1.5148059105975371], \"tvec\": [-0.12750654138887974, -0.10572477988368999, 0.59374010237582], \"width\": 640, \"chess_x\": 5, \"chess_y\": 7, \"chess_square_size_meters\": 0.03}";



    byte[] processedImageData;


    // data from prior intrinsic calibration of HoloLens onboard camera
    private int HoloCam_width = 896;
    private int HoloCam_height = 504;
    private float HoloCam_fx = 1036.9226280151522f;
    private float HoloCam_fy = 1030.7726852885578f;
    private float HoloCam_cx = 418.94203870262618f;
    private float HoloCam_cy = 220.95540290443628f;

    private float HoloCam_k1 = 0.12101663007339994f;
    private float HoloCam_k2 = 0.28401957063644756f;
    private float HoloCam_p1 = 0.00045032788703551724f;
    private float HoloCam_p2 = 0.0089711112978037508f;
    private float HoloCam_k3 = -1.1607259918955408f;



    public int Port = 4434;

#if !NETFX_CORE
    Thread _tcpListenerThread;
#endif

#if NETFX_CORE
    StreamSocketListener listener;
#endif

    const int READ_BUFFER_SIZE = 1048576;
    private Queue<string> incomingMessageQueue = new Queue<string>();
    private object queueLock = new System.Object();




    JSONNode topDownCameraCalibrationData = null;

    bool _processingCameraFrames = false;
    public float ProcessFramesNumSeconds = 20.0f;

    IntPtr _spatialCoordinateSystemPtr;
    VideoPanel _videoPanelUI;
    VideoCapture _videoCapture;
    HoloLensCameraStream.Resolution _resolution;
    CameraParameters _cameraParams;
    byte[] _latestImageBytes;








    // ARUCO native functions
    [DllImport("HoloOpenCVHelper")]
    public static extern void initChessPoseController();
    [DllImport("HoloOpenCVHelper")]
    public static extern void destroyChessPoseController();
    [DllImport("HoloOpenCVHelper")]
    public static extern void newImage(IntPtr imageData);
    [DllImport("HoloOpenCVHelper")]
    public static extern void setImageSize(int row, int col);
    [DllImport("HoloOpenCVHelper")]
    public static extern void detect();
    [DllImport("HoloOpenCVHelper")]
    public static extern IntPtr getProcessedImage();
    [DllImport("HoloOpenCVHelper")]
    public static extern int getNumMarkers();
    [DllImport("HoloOpenCVHelper")]
    public static extern int getSize();
    [DllImport("HoloOpenCVHelper")]
    public static extern int getRows();
    [DllImport("HoloOpenCVHelper")]
    public static extern int getCols();
    [DllImport("HoloOpenCVHelper")]
    public static extern int getInt();












    // Use this for initialization
    void Start () {
        





        //Fetch a pointer to Unity's spatial coordinate system if you need pixel mapping
        _spatialCoordinateSystemPtr = WorldManager.GetNativeISpatialCoordinateSystemPtr();

        _videoPanelUI = GameObject.FindObjectOfType<VideoPanel>();

        //Call this in Start() to ensure that the CameraStreamHelper is already "Awake".
        CameraStreamHelper.Instance.GetVideoCaptureAsync(OnVideoCaptureCreated);

        Debug.Log("Listening for top-down calib messages on port " + Port);

#if NETFX_CORE
        Debug.Log("before starting ListenForMessages_UWP()");
        ListenForMessages_UWP(Port);
        Debug.Log("after starting ListenForMessages_UWP()");
#else
        _tcpListenerThread = new Thread(() => ListenForMessages_UnityEditor(Port));
        _tcpListenerThread.Start();
#endif
    }


    void OnVideoCaptureCreated(VideoCapture videoCapture)
    {
        if (videoCapture == null)
        {
            Debug.LogError("Did not find a video capture object. You may not be using the HoloLens.");
            return;
        }

        this._videoCapture = videoCapture;

        //Request the spatial coordinate ptr if you want fetch the camera and set it if you need to 
        CameraStreamHelper.Instance.SetNativeISpatialCoordinateSystemPtr(_spatialCoordinateSystemPtr);

        _resolution = CameraStreamHelper.Instance.GetLowestResolution();

        processedImageData = new byte[_resolution.height * _resolution.width * 4];

        initChessPoseController();
        setImageSize(_resolution.height, _resolution.width);

        float frameRate = CameraStreamHelper.Instance.GetHighestFrameRate(_resolution);
        //videoCapture.FrameSampleAcquired += OnFrameSampleAcquired;

        //You don't need to set all of these params.
        //I'm just adding them to show you that they exist.
        _cameraParams = new CameraParameters();
        _cameraParams.cameraResolutionHeight = _resolution.height;
        _cameraParams.cameraResolutionWidth = _resolution.width;
        _cameraParams.frameRate = Mathf.RoundToInt(frameRate);
        _cameraParams.pixelFormat = CapturePixelFormat.BGRA32;
        _cameraParams.rotateImage180Degrees = true; //If your image is upside down, remove this line.
        _cameraParams.enableHolograms = false;

        _cameraParams.AutoExposureEnabled = true;
        //_cameraParams.AutoExposureEnabled = false;
        //_cameraParams.ManualExposureAmount = 0.1f;

        UnityEngine.WSA.Application.InvokeOnAppThread(() => { _videoPanelUI.SetResolution(_resolution.width, _resolution.height); }, false);

        Debug.Log("Set up video capture. Ready to record.");



        Debug.Log("DUMMY INIT: mocking input coming in from top-down cam");
        // DUMMY:
        // act as if we have received the mock message...
        incomingMessageQueue.Enqueue(TEST_RECEIVED_MSG_FROM_TOPDOWN);
    }

    private void OnDestroy()
    {
        if (_videoCapture != null)
        {
            //_videoCapture.FrameSampleAcquired -= OnFrameSampleAcquired;
            _videoCapture.Dispose();
        }

#if NETFX_CORE
        if (listener != null)
        {
            listener.Dispose();
            listener = null;
        }
#else
        if (_tcpListenerThread != null)
        {
            _tcpListenerThread.Abort();
            _tcpListenerThread = null;
        }
#endif

        destroyChessPoseController();
    }

    private IEnumerator ProcessCameraFrames(float numSeconds)
    {
        StartCameraProcessing();

        yield return new WaitForSeconds(numSeconds);

        StopCameraProcessing();
    }

    private void StartCameraProcessing()
    {
        Debug.Log("Starting camera processing...");

        if (!_processingCameraFrames)
        {
            this._videoCapture.StartVideoModeAsync(_cameraParams, OnVideoModeStarted);
        }
    }

    private void OnVideoModeStarted(VideoCaptureResult result)
    {
        if (result.success == false)
        {
            Debug.LogError("Could not start video mode");
            return;
        }

        _processingCameraFrames = true;

        if (_processingCameraFrames)
        {
            this._videoCapture.RequestNextFrameSample(OnFrameSampleAcquired);
        }
    }

    void OnFrameSampleAcquired(VideoCaptureSample sample)
    {
        //When copying the bytes out of the buffer, you must supply a byte[] that is appropriately sized.
        //You can reuse this byte[] until you need to resize it (for whatever reason).
        if (_latestImageBytes == null || _latestImageBytes.Length < sample.dataLength)
        {
            _latestImageBytes = new byte[sample.dataLength];
        }
        sample.CopyRawImageDataIntoBuffer(_latestImageBytes);

        //If you need to get the cameraToWorld matrix for purposes of compositing you can do it like this
        float[] cameraToWorldMatrixAsFloat;
        if (sample.TryGetCameraToWorldMatrix(out cameraToWorldMatrixAsFloat) == false)
        {
            return;
        }

        //If you need to get the projection matrix for purposes of compositing you can do it like this
        float[] projectionMatrixAsFloat;
        if (sample.TryGetProjectionMatrix(out projectionMatrixAsFloat) == false)
        {
            return;
        }

        // Right now we pass things across the pipe as a float array then convert them back into UnityEngine.Matrix using a utility method
        Matrix4x4 cameraToWorldMatrix = LocatableCameraUtils.ConvertFloatArrayToMatrix4x4(cameraToWorldMatrixAsFloat);
        Matrix4x4 projectionMatrix = LocatableCameraUtils.ConvertFloatArrayToMatrix4x4(projectionMatrixAsFloat);

        //This is where we actually use the image data
        UnityEngine.WSA.Application.InvokeOnAppThread(() =>
        {
            _videoPanelUI.SetBytes(_latestImageBytes);

            Texture2D tex = _videoPanelUI.rawImage.texture as Texture2D;

            Color32[] c = tex.GetPixels32();
            IntPtr imageHandle = getImageHandle(c);
            newImage(imageHandle);

            // do any detection here...

            //// Fetch the processed image and render
            imageHandle = getProcessedImage();
            Marshal.Copy(imageHandle, processedImageData, 0, _resolution.width * _resolution.height * 4);
            tex.LoadRawTextureData(processedImageData);
            tex.Apply();



            Vector3 cameraWorldPosition = cameraToWorldMatrix.MultiplyPoint(Vector3.zero);
            Quaternion cameraWorldRotation = Quaternion.LookRotation(-cameraToWorldMatrix.GetColumn(2), cameraToWorldMatrix.GetColumn(1));
            
            if (_processingCameraFrames)
            {
                this._videoCapture.RequestNextFrameSample(OnFrameSampleAcquired);
            }
        }, false);
    }

    private void StopCameraProcessing()
    {
        Debug.Log("Stopping camera processing...");

        if (_processingCameraFrames)
        {
            this._videoCapture.StopVideoModeAsync(OnVideoModeStopped);
        }
    }

    private static IntPtr getImageHandle(Color32[] colors)
    {
        IntPtr ptr;
        GCHandle handle = default(GCHandle);
        try
        {
            handle = GCHandle.Alloc(colors, GCHandleType.Pinned);
            ptr = handle.AddrOfPinnedObject();
        }
        finally
        {
            if (handle != default(GCHandle))
                handle.Free();
        }
        return ptr;
    }

    private void OnVideoModeStopped(VideoCaptureResult result)
    {
        if (result.success)
        {
            Debug.Log("Stopped video mode");

            _processingCameraFrames = false;
        } else
        {
            Debug.LogError("Failed to stop video mode");
        }
    }




    // Update is called once per frame
    void Update () {
        Queue<string> tempqueue;
        lock (queueLock)
        {
            tempqueue = incomingMessageQueue;
            incomingMessageQueue = new Queue<string>();
        }
        foreach (var msg in tempqueue)
        {
            Debug.Log(String.Format("Read Message from top-down camera helper: {0}", msg));

            try
            {
                var jsonObj = JSON.Parse(msg);

                if (jsonObj != null)
                {
                    topDownCameraCalibrationData = jsonObj;

                    if (!_processingCameraFrames)
                    {
                        StartCoroutine(ProcessCameraFrames(ProcessFramesNumSeconds));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError(string.Format("Failed to parse incoming message as JSON: {0}", e));
            }

            // can do something with the incoming data in this thread
        }
	}

#if NETFX_CORE
    public async void ListenForMessages_UWP(int port)
    {
        Debug.Log("start ListenForMessages_UWP");
        listener = new StreamSocketListener();
        listener.ConnectionReceived += OnConnection_UWP;

        listener.Control.KeepAlive = false;

        string localServiceName = port.ToString();

        Debug.Log("localServiceName: " + localServiceName);

        try
        {
            Debug.Log("before BindServiceNameAsync");
            await listener.BindServiceNameAsync(localServiceName);
            Debug.Log("after BindServiceNameAsync");
        }
        catch (Exception exception)
        {
            // If this is an unknown status it means that the error is fatal and retry will likely fail.
            if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
            {
                throw;
            }

            Debug.LogError("Start listening failed with error: " + exception.Message);
        }
    }

    private async void OnConnection_UWP(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        Debug.Log("start OnConnection_UWP");
        StringBuilder strBuilder;
        DataReader reader;
        using (reader = new DataReader(args.Socket.InputStream))
        {
            try
            {
                while (true)
                {
                    strBuilder = new StringBuilder();

                    // Set the DataReader to only wait for available data (so that we don't have to know the data size)
                    reader.InputStreamOptions = Windows.Storage.Streams.InputStreamOptions.Partial;
                    // The encoding and byte order need to match the settings of the writer we previously used.
                    reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                    reader.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;

                    // Send the contents of the writer to the backing stream. 
                    // Get the size of the buffer that has not been read.
                    await reader.LoadAsync(256);

                    // Keep reading until we consume the complete stream.
                    while (reader.UnconsumedBufferLength > 0)
                    {
                        strBuilder.Append(reader.ReadString(reader.UnconsumedBufferLength));
                        await reader.LoadAsync(256);
                    }

                    reader.DetachStream();

                    string receivedString = strBuilder.ToString();

                    Debug.Log(String.Format("Received: {0}", receivedString));

                    // here we could transform the incoming data into another format
                    lock (queueLock)
                    {
                        incomingMessageQueue.Enqueue(receivedString);
                    }
                }
            }
            catch (Exception exception)
            {
                // If this is an unknown status it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                Debug.LogError("Read stream failed with error: " + exception.Message);
            }
        }
        
    }
#else
    public void ListenForMessages_UnityEditor(int port)
    {
        TcpListener server = null;
        try
        {
            IPAddress localAddr = IPAddress.Parse("127.0.0.1");

            server = new TcpListener(localAddr, port);

            // start listening for client requests.
            server.Start();

            Byte[] bytes = new Byte[READ_BUFFER_SIZE];
            String data = null;

            // enter the listening loop
            while (true)
            {
                Debug.Log(String.Format("Waiting for a connection on port {0}...", port));

                // perform a blocking call to accept requests.
                using (TcpClient client = server.AcceptTcpClient())
                {
                    Debug.Log("Remote client connected");

                    data = null;

                    // get a stream object for reading/writing
                    NetworkStream stream = client.GetStream();

                    int i;

                    // loop to receive all data sent by client
                    while ((i = stream.Read(bytes, 0, bytes.Length)) != 0)
                    {
                        // translate data bytes into (UTF8) string
                        data = System.Text.Encoding.UTF8.GetString(bytes, 0, i);
                        Debug.Log(String.Format("Received: {0}", data));

                        // here we could transform the incoming data into another format
                        lock (queueLock)
                        {
                            incomingMessageQueue.Enqueue(data);
                        }

                        // here we could send back a response
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError(String.Format("Exception: {0}", e));
        }
        finally
        {
            // stop listening for new clients
            server.Stop();
        }
    }
#endif

}
