using System;
using System.Runtime.InteropServices;

namespace TcpUdpForwarder.Utils
{
    public sealed class SharedMemory : IDisposable
    {
        #region Const

        private const int TIMEOUT_TICK = 600; // Receive package time out
        private const int SLEEP_TICK = 10; // check shared memory per 10ms
        private const int PACKAGE_SIZE = 4096; // max size of data in one package

        private const int FLAG_IDLE = 0;
        private const int FLAG_READY_READ = 2;

        #endregion

        #region Private Members

        private string _Name;
        private string _SharedMemName;
        private SharedMemoryState _State;
        private Semaphore _Semaphore;
        private ShareMem _ShareMem;
        private string _ID;
        private string _ServerID;
        private object _Locker;
        private System.Collections.Generic.Queue<Message> _Queue;
        private object _QueueLocker;

        #endregion

        #region Static

        public const string EMPTY_ID = "00000000000000000000000000000000";
        /// <summary>
        /// Return 32 length GUID ID
        /// </summary>
        /// <returns></returns>
        public static string NewID()
        {
            return Guid.NewGuid().ToString("N").ToUpper();
        }

        public static bool IsServerStart(string sharedMemName)
        {
            bool isStart = false;
            Semaphore semaphore = null;
            try
            {
                semaphore = new Semaphore(sharedMemName + "_Semaphore");
                isStart = semaphore.AlreadyExist;
            }
            finally
            {
                if (semaphore != null)
                {
                    semaphore.Dispose();
                    semaphore = null;
                }
            }
            return isStart;
        }

        #endregion

        public SharedMemory(string sharedMemName)
            : this(string.Empty, sharedMemName)
        {
        }

        public SharedMemory(string name, string sharedMemName)
        {
            this._Name = name;
            this._SharedMemName = sharedMemName;
            this._State = SharedMemoryState.Idle;
            this._Semaphore = null;
            this._ShareMem = null;
            this._ID = SharedMemory.NewID();
            this._ServerID = SharedMemory.EMPTY_ID;
            this._Locker = new object();
            this._Queue = new System.Collections.Generic.Queue<Message>();
            this._QueueLocker = new object();
        }

        ~SharedMemory()
        {
            this.Dispose(false);
        }

        #region Public Properties

        public event EventHandler<ReceivedEventArgs> OnReceived;

        public event EventHandler<SendEventArgs> OnSend;

        public event EventHandler<ErrorEventArgs> OnError;

        public string Name
        {
            get { return this._Name; }
            set { this._Name = value; }
        }

        public string SharedMemName
        {
            get { return this._SharedMemName; }
        }

        public string ID
        {
            get { return this._ID; }
        }

        public SharedMemoryState State
        {
            get { return this._State; }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Start the Server
        /// </summary>
        public void Listen()
        {
            lock (this._Locker)
            {
                if (this._Semaphore != null)
                    throw new Exception("The SharedMemory is in listening or already connected to the server.");
                this._Semaphore = new Semaphore(this._SharedMemName + "_Semaphore");
                if (this._Semaphore.AlreadyExist)
                {
                    this._Semaphore.Dispose();
                    this._Semaphore = null;
                    this._State = SharedMemoryState.Idle;
                    throw new Exception("The SharedMemory already exists. Name: " + this._SharedMemName);
                }
                this._ShareMem = new ShareMem(this.SharedMemName, Marshal.SizeOf(typeof(ShareMemData)));
                try
                {
                    this._ShareMem.Listen();
                }
                catch (Exception ex)
                {
                    this._Semaphore.Dispose();
                    this._Semaphore = null;
                    this._ShareMem.Dispose();
                    this._ShareMem = null;
                    this._State = SharedMemoryState.Idle;
                    throw ex;
                }
                this._ServerID = this._ID;
                ShareMemData data = new ShareMemData();
                data.ServerID = this._ServerID;
                data.From = this._ID;
                data.To = SharedMemory.EMPTY_ID;
                data.Flag = SharedMemory.FLAG_IDLE;
                data.PackageID = SharedMemory.NewID();
                data.PackageCount = 0;
                data.PackageIndex = 0;
                byte[] temp = StructSerializeTools.StructToBytes(data);
                this._Semaphore.Lock();
                this._ShareMem.Write(temp);
                this._Semaphore.Unlock();

                System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(this.Task));
                this._State = SharedMemoryState.Listening;
            }
        }

        /// <summary>
        /// Connect to the Server
        /// </summary>
        public void Connect()
        {
            lock (this._Locker)
            {
                if (this._Semaphore != null)
                    throw new Exception("The SharedMemory is in listening or already connected to the server.");
                this._State = SharedMemoryState.Connecting;
                this._Semaphore = new Semaphore(this._SharedMemName + "_Semaphore");
                if (!this._Semaphore.AlreadyExist)
                {
                    this._Semaphore.Dispose();
                    this._Semaphore = null;
                    this._State = SharedMemoryState.Idle;
                    throw new Exception("The SharedMemory not exists. Name: " + this._SharedMemName);
                }
                this._ShareMem = new ShareMem(this.SharedMemName, Marshal.SizeOf(typeof(ShareMemData)));
                try
                {
                    this._ShareMem.Connect();
                }
                catch (Exception ex)
                {
                    this._Semaphore.Dispose();
                    this._Semaphore = null;
                    this._ShareMem.Dispose();
                    this._ShareMem = null;
                    this._State = SharedMemoryState.Idle;
                    throw ex;
                }

                byte[] temp = null;
                ShareMemData data;
                this._Semaphore.Lock();
                temp = this._ShareMem.Read();
                this._Semaphore.Unlock();
                data = (ShareMemData)StructSerializeTools.BytesToStuct(temp, typeof(ShareMemData));
                this._ServerID = data.ServerID;

                System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(this.Task));
                this._State = SharedMemoryState.Connected;
            }
        }

        /// <summary>
        /// start the server. if the server have been exists then convert the exists server to client.
        /// </summary>
        public void ForceListen()
        {
            lock (this._Locker)
            {
                if (this._Semaphore != null)
                    throw new Exception("The SharedMemory is in listening or already connected to the server.");
                this._Semaphore = new Semaphore(this._SharedMemName + "_Semaphore");
                this._ShareMem = new ShareMem(this.SharedMemName, Marshal.SizeOf(typeof(ShareMemData)));
                try
                {
                    if (this._Semaphore.AlreadyExist)
                        this._ShareMem.Connect();
                    else
                        this._ShareMem.Listen();
                }
                catch (Exception ex)
                {
                    this._Semaphore.Dispose();
                    this._Semaphore = null;
                    this._ShareMem.Dispose();
                    this._ShareMem = null;
                    this._State = SharedMemoryState.Idle;
                    throw ex;
                }
                this._ServerID = this._ID;
                ShareMemData data = new ShareMemData();
                data.ServerID = this._ServerID;
                data.From = this._ID;
                data.To = SharedMemory.EMPTY_ID;
                data.Flag = SharedMemory.FLAG_IDLE;
                data.PackageID = SharedMemory.NewID();
                data.PackageCount = 0;
                data.PackageIndex = 0;
                byte[] temp = StructSerializeTools.StructToBytes(data);
                this._Semaphore.Lock();
                this._ShareMem.Write(temp);
                this._Semaphore.Unlock();

                System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(this.Task));
                this._State = SharedMemoryState.Listening;
            }
        }

        public void ConvertToServer()
        {
            byte[] temp = null;
            ShareMemData data;

            this._Semaphore.Lock();
            temp = this._ShareMem.Read();
            data = (ShareMemData)StructSerializeTools.BytesToStuct(temp, typeof(ShareMemData));
            this._ServerID = this._ID;
            data.ServerID = this._ServerID;
            temp = StructSerializeTools.StructToBytes(data);
            this._ShareMem.Write(temp);
            this._Semaphore.Unlock();

            this._State = SharedMemoryState.Listening;
        }

        /// <summary>
        /// Close
        /// </summary>
        public void Close()
        {
            lock (this._Locker)
            {
                this._State = SharedMemoryState.Closing;

                if (this._ShareMem != null)
                {
                    this._ShareMem.Dispose();
                    this._ShareMem = null;
                }

                if (this._Semaphore != null)
                {
                    this._Semaphore.Dispose();
                    this._Semaphore = null;
                }

                this._State = SharedMemoryState.Closed;
            }
        }

        /// <summary>
        /// Send the data to Server
        /// </summary>
        /// <param name="bytData"></param>
        public void Send(byte[] bytData)
        {
            this.Send(this._ServerID, bytData);
        }

        /// <summary>
        /// Send the data to Client
        /// </summary>
        /// <param name="to"></param>
        /// <param name="bytData"></param>
        public void Send(string to, byte[] bytData)
        {
            lock (this._QueueLocker)
            {
                Message msg = new Message(to, bytData);
                this._Queue.Enqueue(msg);
            }
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Methods

        private void Task(object state)
        {
            byte[] temp = null;
            Message msg = null;
            string lastPackageID = string.Empty;
            int lastPackageIndex = 0;
            int lastPackageDupCount = 0;
            System.Collections.Hashtable packageCache = new System.Collections.Hashtable();
            PackageFactory pf = null;
            int checkPackageCacheLeft = SharedMemory.TIMEOUT_TICK;
            ShareMemData data;
            Exception exception = null;
            Exception[] exceptions = null;
            bool stop = false;
            while (!stop && this._State != SharedMemoryState.Closing && this._State != SharedMemoryState.Closed)
            {
                if (this._Semaphore == null || this._ShareMem == null || this._Locker == null)
                    break;
                lock (this._Locker)
                {
                    if (this._Semaphore == null || this._ShareMem == null)
                        break;
                    checkPackageCacheLeft--;
                    this._Semaphore.Lock();
                    try
                    {
                        temp = this._ShareMem.Read();
                        data = (ShareMemData)StructSerializeTools.BytesToStuct(temp, typeof(ShareMemData));
                        if (data.ServerID != this._ServerID)
                        {
                            this._ServerID = data.ServerID;
                            if (this._State == SharedMemoryState.Listening)
                                this._State = SharedMemoryState.Connected;
                        }
                        if (data.Flag == SharedMemory.FLAG_READY_READ
                            && (data.To == this._ID || (data.To == SharedMemory.EMPTY_ID && this._State == SharedMemoryState.Listening)))
                        {
                            #region Process Message

                            #region Read Data

                            if (data.PackageCount == 1)
                            {
                                if (data.Size > 0)
                                {
                                    temp = new byte[data.Size];
                                    Buffer.BlockCopy(data.Data, 0, temp, 0, temp.Length);
                                    ThrowOnReceived(data.From, data.PackageID, temp);
                                }
                            }
                            else if (data.PackageCount > 0)
                            {
                                if (data.Size > 0)
                                {
                                    temp = new byte[data.Size];
                                    Buffer.BlockCopy(data.Data, 0, temp, 0, temp.Length);
                                    if (packageCache.ContainsKey(data.PackageID))
                                        pf = packageCache[data.PackageID] as PackageFactory;
                                    else
                                    {
                                        pf = new PackageFactory(data.PackageID, data.From, data.PackageCount);
                                        packageCache.Add(data.PackageID, pf);
                                    }
                                    pf.Add(data.PackageIndex, temp);
                                    if (pf.IsCompleted())
                                    {
                                        packageCache.Remove(pf.PackageID);
                                        ThrowOnReceived(pf.From, pf.PackageID, pf.ReadAll());
                                    }
                                    pf = null;
                                }
                            }
                            #endregion

                            #endregion

                            this.SetIdle(ref data);
                        }
                        else if (data.Flag == SharedMemory.FLAG_IDLE && (msg != null || this.HasSendData()))
                        {

                            #region Send Message

                            if (msg == null)
                            {
                                msg = PopSendData();
                                if (!msg.MoveNext())
                                    continue;
                            }
                            // Send Data
                            data.To = msg.To;
                            data.From = this._ID;
                            data.Flag = SharedMemory.FLAG_READY_READ;
                            data.PackageID = msg.PackageID;
                            data.PackageCount = msg.PackageCount;
                            data.PackageIndex = msg.PackageIndex;
                            temp = msg.ReadPage();
                            data.Size = temp.Length;
                            Buffer.BlockCopy(temp, 0, data.Data, 0, temp.Length);
                            temp = StructSerializeTools.StructToBytes(data);
                            this._ShareMem.Write(temp);
                            if (!msg.MoveNext())
                            {
                                ThrowOnSend(msg);
                                msg = null;
                            }

                            #endregion

                        }
                        else
                        {
                            #region Check Timeout Packages

                            if (checkPackageCacheLeft < 1)
                            {
                                if (packageCache.Count > 0)
                                {
                                    System.Collections.Generic.List<string> keys = new System.Collections.Generic.List<string>();
                                    foreach (string key in packageCache)
                                    {
                                        pf = packageCache[key] as PackageFactory;
                                        if (pf.IsTimeout())
                                        {
                                            keys.Add(key);
                                        }
                                    }
                                    if (keys.Count > 0)
                                    {
                                        exceptions = new Exception[keys.Count];
                                        for (int i = 0; i < keys.Count; i++)
                                        {
                                            pf = packageCache[keys[i]] as PackageFactory;
                                            packageCache.Remove(keys[i]);
                                            exceptions[i] = new TimeOutException(string.Format("Receive Package {0} Time Out", pf.PackageID), pf.From, this._ID, pf.PackageID, null);
                                        }
                                    }
                                }
                                checkPackageCacheLeft = SharedMemory.TIMEOUT_TICK;
                            }

                            #endregion

                            if (lastPackageID == data.PackageID && lastPackageIndex == data.PackageIndex && data.Flag != SharedMemory.FLAG_IDLE)
                            {
                                if (lastPackageDupCount > 10 && (data.From == this._ID || this._State == SharedMemoryState.Listening))
                                {

                                    #region Throw Time out Exception

                                    if (data.Size > 0 && data.PackageCount > 0)
                                    {
                                        temp = new byte[data.Size];
                                        Buffer.BlockCopy(data.Data, 0, temp, 0, temp.Length);
                                    }
                                    else
                                    {
                                        temp = null;
                                    }
                                    if (data.From == this._ID)
                                    {
                                        this.SetIdle(ref data);
                                        throw new TimeOutException(string.Format("Send Package {0} Time Out", data.PackageID), data.From, data.To, data.PackageID, temp);
                                    }
                                    else if (this._State == SharedMemoryState.Listening)
                                    {
                                        this.SetIdle(ref data);
                                        throw new TimeOutException(string.Format("The package {0} have no client to process", data.PackageID), data.From, data.To, data.PackageID, temp);
                                    }
                                    else
                                    {
                                        this.SetIdle(ref data);
                                    }
                                    #endregion
                                }
                                else if (lastPackageDupCount > 100)
                                {

                                    #region Throw Time out Exception

                                    if (data.Size > 0 && data.PackageCount > 0)
                                    {
                                        temp = new byte[data.Size];
                                        Buffer.BlockCopy(data.Data, 0, temp, 0, temp.Length);
                                    }
                                    else
                                    {
                                        temp = null;
                                    }
                                    this.SetIdle(ref data);
                                    throw new TimeOutException(string.Format("The package {0} have no client and server to process", data.PackageID), data.From, data.To, data.PackageID, temp);

                                    #endregion
                                }
                                else
                                {
                                    lastPackageDupCount++;
                                }
                            }
                            else
                            {
                                lastPackageID = data.PackageID;
                                lastPackageIndex = data.PackageIndex;
                                lastPackageDupCount = 0;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                    finally
                    {
                        this._Semaphore.Unlock();
                    }

                    #region Process Exceptions

                    if (exception != null)
                    {
                        if (ThrowOnError(exception))
                            stop = true;
                        exception = null;
                    }

                    if (exceptions != null)
                    {
                        for (int i = 0; i < exceptions.Length; i++)
                        {
                            if (ThrowOnError(exceptions[i]))
                            {
                                stop = true;
                                break;
                            }
                        }
                        exceptions = null;
                    }

                    #endregion

                    if (!stop && SharedMemory.SLEEP_TICK > 0)
                        System.Threading.Thread.Sleep(SharedMemory.SLEEP_TICK);
                }
            }
        }

        private void SetIdle(ref ShareMemData data)
        {
            #region Set IDLE

            data.To = SharedMemory.EMPTY_ID;
            data.From = this._ID;
            data.Flag = SharedMemory.FLAG_IDLE;
            data.PackageID = SharedMemory.NewID();
            data.PackageCount = 0;
            data.PackageIndex = 0;
            byte[] temp = StructSerializeTools.StructToBytes(data);
            this._ShareMem.Write(temp);

            #endregion
        }

        private bool HasSendData()
        {
            lock (this._QueueLocker)
            {
                return this._Queue.Count > 0;
            }
        }

        private Message PopSendData()
        {
            lock (this._QueueLocker)
            {
                Message msg = this._Queue.Dequeue();
                return msg;
            }
        }

        private void ThrowOnReceived(string from, string packageID, byte[] data)
        {
            if (OnReceived != null)
            {
                OnReceived(this, new ReceivedEventArgs(from, packageID, data));
            }
        }

        private void ThrowOnSend(Message msg)
        {
            if (OnSend != null)
            {
                OnSend(this, new SendEventArgs(msg.To, msg.PackageID, msg.Data));
            }
        }

        private bool ThrowOnError(Exception ex)
        {
            if (OnError != null)
            {
                ErrorEventArgs args = new ErrorEventArgs(ex);
                OnError(this, args);
                return args.Cancel;
            }
            return false;
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected void Dispose(bool disposing)
        {
            // Clean up non-managed resources
            this.Close();
            if (disposing)
            {
                // Clean up managed resources
                this._SharedMemName = null;
                this._ID = null;
                this._ServerID = null;
                this._Queue.Clear();
                this._Queue = null;
                this._Locker = null;
            }
        }

        #endregion

        #region Classes

        #region class TimeOutException

        public class TimeOutException : Exception
        {
            private string _From;
            private string _To;
            private string _PackageID;
            private byte[] _Bytes;

            public TimeOutException(string message, string from, string to, string packageID, byte[] data)
                : base(message)
            {
                this._From = from;
                this._To = to;
                this._PackageID = packageID;
                this._Bytes = data;
            }

            public string From
            {
                get { return this._From; }
            }

            public string To
            {
                get { return this._To; }
            }

            public string PackageID
            {
                get { return this._PackageID; }
            }

            public byte[] Bytes
            {
                get { return this._Bytes; }
            }

        }

        #endregion

        #region class ReceivedEventArgs

        public class ReceivedEventArgs : EventArgs
        {
            private string _From;
            private string _PackageID;
            private byte[] _Data;

            public ReceivedEventArgs(string from, string packageID, byte[] data)
                : base()
            {
                this._From = from;
                this._PackageID = packageID;
                this._Data = data;
            }

            public string From
            {
                get { return this._From; }
            }

            public string PackageID
            {
                get { return this._PackageID; }
            }

            public byte[] Data
            {
                get { return this._Data; }
            }

        }

        #endregion

        #region class SendEventArgs

        public class SendEventArgs : EventArgs
        {
            private string _To;
            private string _PackageID;
            private byte[] _Data;

            public SendEventArgs(string to, string packageID, byte[] data)
                : base()
            {
                this._To = to;
                this._PackageID = packageID;
                this._Data = data;
            }

            public string To
            {
                get { return this._To; }
            }

            public string PackageID
            {
                get { return this._PackageID; }
            }

            public byte[] Data
            {
                get { return this._Data; }
            }

        }

        #endregion

        #region class ErrorEventArgs

        public class ErrorEventArgs : EventArgs
        {
            private Exception _Exception;
            private bool _Cancel;

            public ErrorEventArgs(Exception ex)
                : base()
            {
                this._Exception = ex;
                this._Cancel = false;
            }

            public Exception Exception
            {
                get { return this._Exception; }
            }

            public bool Cancel
            {
                get { return this._Cancel; }
                set { this._Cancel = value; }
            }

        }

        #endregion

        #region enum SharedMemoryState

        public enum SharedMemoryState
        {
            Idle,
            Listening,
            Closed,
            Closing,
            Connecting,
            Connected,

        }

        #endregion

        #region class PackageFactory

        private class PackageFactory
        {
            public string PackageID;
            public string From;
            private DateTime LastTime;
            private Package[] Data;

            public PackageFactory(string id, string from, int size)
            {
                this.PackageID = id;
                this.From = from;
                this.Data = new Package[size];
                this.LastTime = DateTime.Now;
            }

            public bool IsCompleted()
            {
                for (int i = this.Data.Length - 1; i >= 0; i--)
                {
                    if (this.Data[i] == null)
                        return false;
                }
                return true;
            }

            public bool IsTimeout()
            {
                if (this.IsCompleted())
                    return false;
                TimeSpan ts = DateTime.Now - this.LastTime;
                return ts.TotalSeconds > 20;
            }

            public byte[] ReadAll()
            {
                System.Collections.Generic.List<byte> bl = new System.Collections.Generic.List<byte>();
                for (int i = 0; i < this.Data.Length; i++)
                {
                    bl.AddRange(this.Data[i].Data);
                }
                return bl.ToArray();
            }

            public void Add(int index, byte[] data)
            {
                if (index < 0 || index >= this.Data.Length)
                    return;
                this.Data[index] = new Package(data);
                this.LastTime = DateTime.Now;
            }

            private class Package
            {
                public byte[] Data;

                public Package(byte[] data)
                {
                    this.Data = data;
                }
            }
        }

        #endregion

        #region class Message

        private class Message
        {
            public string PackageID;
            public string To;
            public byte[] Data;
            public int PackageCount;
            public int PackageIndex;

            public Message(string to, byte[] data)
                : this(SharedMemory.NewID(), to, data)
            {
            }

            public Message(string id, string to, byte[] data)
            {
                this.PackageID = id;
                this.To = to;
                this.Data = data;
                this.PackageIndex = -1;
                this.PackageCount = data.Length / SharedMemory.PACKAGE_SIZE;
                if (data.Length % SharedMemory.PACKAGE_SIZE != 0)
                    this.PackageCount++;
            }

            public bool MoveNext()
            {
                this.PackageIndex++;
                return this.PackageCount > 0 && this.PackageIndex < this.PackageCount;
            }

            public byte[] ReadPage()
            {
                int len = SharedMemory.PACKAGE_SIZE;
                int start = this.PackageIndex * len;
                if (start + len > this.Data.Length)
                    len = this.Data.Length - start;
                byte[] bs = new byte[len];
                Buffer.BlockCopy(this.Data, start, bs, 0, len);
                return bs;
            }

            public void Reset()
            {
                this.PackageIndex = -1;
                this.PackageCount = this.Data.Length / SharedMemory.PACKAGE_SIZE;
                if (this.Data.Length % SharedMemory.PACKAGE_SIZE != 0)
                    this.PackageCount++;
            }
        }

        #endregion

        #region struct ShareMemData

        [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
        private struct ShareMemData
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
            public string ServerID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
            public string From;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
            public string To;
            [MarshalAs(UnmanagedType.I4)]
            public int Flag;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
            public string PackageID;
            [MarshalAs(UnmanagedType.I4)]
            public int PackageIndex;
            [MarshalAs(UnmanagedType.I4)]
            public int PackageCount;
            [MarshalAs(UnmanagedType.I4)]
            public int Size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = SharedMemory.PACKAGE_SIZE)]
            public byte[] Data;

            //public ShareMemData()
            //    : this(0)
            //{
            //}

            public ShareMemData(int flag)
            {
                this.ServerID = SharedMemory.EMPTY_ID;
                this.From = SharedMemory.EMPTY_ID;
                this.To = SharedMemory.EMPTY_ID;
                this.Flag = flag;
                this.PackageID = SharedMemory.EMPTY_ID;
                this.PackageCount = 0;
                this.PackageIndex = 0;
                this.Size = 0;
                this.Data = new byte[SharedMemory.PACKAGE_SIZE];
            }

        }

        #endregion

        #region class StructSerializeTools

        private static class StructSerializeTools
        {
            //Serialize Struct
            public static byte[] StructToBytes(object structObj)
            {
                //Get the size of Struct
                int size = Marshal.SizeOf(structObj);
                byte[] bytes = new byte[size];
                //Alloc memory
                IntPtr structPtr = Marshal.AllocHGlobal(size);
                //Copy struct object to alloced memory
                Marshal.StructureToPtr(structObj, structPtr, false);
                //Copy alloced memory data to byte array
                Marshal.Copy(structPtr, bytes, 0, size);
                //Free alloced memory
                Marshal.FreeHGlobal(structPtr);
                return bytes;
            }

            //Deserialize Struct
            public static object BytesToStuct(byte[] bytes, Type type)
            {
                //Get the size of Struct
                int size = Marshal.SizeOf(type);
                if (size > bytes.Length)
                    return null;
                //Alloc memory
                IntPtr structPtr = Marshal.AllocHGlobal(size);
                //Copy byte array to alloced memory
                Marshal.Copy(bytes, 0, structPtr, size);
                //Convert alloced memory to a Struct
                object obj = Marshal.PtrToStructure(structPtr, type);
                //Free alloced memory
                Marshal.FreeHGlobal(structPtr);
                return obj;
            }
        }

        #endregion

        #region class Semaphore

        public class Semaphore : IDisposable
        {
            #region Private Members

            private string _Name;
            private IntPtr _Handle = IntPtr.Zero;
            private bool _AlreadyExist;

            #endregion

            public Semaphore(string name)
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentNullException("name", "name cannot be null or empty.");
                this._Name = name;
                this._Handle = NativeMethods.CreateSemaphoreW(IntPtr.Zero, 1, 1, this._Name);
                if (this._Handle == IntPtr.Zero)
                    throw new Exception("Cannot create Semaphore. Error: " + NativeMethods.GetLastError().ToString());
                if (NativeMethods.GetLastError() == NativeMethods.ERROR_ALREADY_EXISTS)
                    this._AlreadyExist = true;
                else
                    this._AlreadyExist = false;
            }

            ~Semaphore()
            {
                Dispose(false);
            }

            #region Public Properties

            public string Name
            {
                get { return this._Name; }
            }

            public bool AlreadyExist
            {
                get { return this._AlreadyExist; }
            }

            #endregion

            #region Public Methods

            public void Lock()
            {
                if (this._Handle != IntPtr.Zero)
                    NativeMethods.WaitForSingleObject(this._Handle, 0xFFFFFFFF);
            }

            public void Unlock()
            {
                if (this._Handle != IntPtr.Zero)
                    NativeMethods.ReleaseSemaphore(this._Handle, 1, IntPtr.Zero);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            #endregion

            #region Protected Methods

            /// <summary>
            /// Clean up any resources being used.
            /// </summary>
            /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
            protected void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // Clean up managed resources
                }
                // Clean up non-managed resources
                if (this._Handle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(this._Handle);
                    this._Handle = IntPtr.Zero;
                }
            }

            #endregion

        }

        #endregion

        #region class ShareMem

        public class ShareMem : IDisposable
        {
            #region Private Members

            private string _Name;
            private int _Size;
            private bool _AlreadyExist;
            private IntPtr _SharedMemoryFile = IntPtr.Zero;
            private IntPtr _Data = IntPtr.Zero;

            #endregion

            #region Public Properties

            public string Name
            {
                get { return this._Name; }
            }

            public int Size
            {
                get { return this._Size; }
            }

            public bool AlreadyExist
            {
                get { return this._AlreadyExist; }
            }

            #endregion

            public ShareMem(string name, int size)
            {
                if (string.IsNullOrEmpty(name))
                    throw new ArgumentNullException("name", "name cannot be null or empty.");
                if (size < 4)
                    throw new ArgumentNullException("size", "size cannot be less than 4.");
                this._Name = name;
                this._Size = size;
                this._AlreadyExist = false;
            }

            ~ShareMem()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Clean up any resources being used.
            /// </summary>
            /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
            protected void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // Clean up managed resources
                }
                // Clean up non-managed resources
                Close();
            }

            public void Listen()
            {
                //Create a shared memory (INVALID_HANDLE_VALUE)
                this._SharedMemoryFile = NativeMethods.CreateFileMapping(NativeMethods.INVALID_HANDLE_VALUE, IntPtr.Zero, (uint)NativeMethods.PAGE_READWRITE, 0, (uint)this._Size, this._Name);
                if (this._SharedMemoryFile == IntPtr.Zero)
                    throw new Exception("Cannot create file mapping. Name: " + this._Name + ", Size:" + this._Size.ToString());
                if (NativeMethods.GetLastError() == NativeMethods.ERROR_ALREADY_EXISTS)
                    throw new Exception("The file mapping already exists. Name: " + this._Name + ", Size:" + this._Size.ToString());
                this._AlreadyExist = false;
                //Create memory map
                this._Data = NativeMethods.MapViewOfFile(this._SharedMemoryFile, NativeMethods.FILE_MAP_WRITE, 0, 0, (uint)this._Size);
                if (this._Data == IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(this._SharedMemoryFile);
                    this._SharedMemoryFile = IntPtr.Zero;
                    throw new Exception("Cannot map view of file. Name: " + this._Name + ", Size:" + this._Size.ToString());
                }

                byte[] bytData = new byte[this._Size];
                Marshal.Copy(this._Data, bytData, 0, bytData.Length);
            }

            public void Connect()
            {
                //Create a shared memory (INVALID_HANDLE_VALUE)
                this._SharedMemoryFile = NativeMethods.CreateFileMapping(NativeMethods.INVALID_HANDLE_VALUE, IntPtr.Zero, (uint)NativeMethods.PAGE_READWRITE, 0, (uint)this._Size, this._Name);
                if (this._SharedMemoryFile == IntPtr.Zero)
                    throw new Exception("Cannot create file mapping. Name: " + this._Name + ", Size:" + this._Size.ToString());
                if (NativeMethods.GetLastError() != NativeMethods.ERROR_ALREADY_EXISTS)
                {
                    NativeMethods.CloseHandle(this._SharedMemoryFile);
                    this._SharedMemoryFile = IntPtr.Zero;
                    throw new Exception("The file mapping is not exists. Name: " + this._Name + ", Size:" + this._Size.ToString());
                }
                this._AlreadyExist = true;
                //Create memory map
                this._Data = NativeMethods.MapViewOfFile(this._SharedMemoryFile, NativeMethods.FILE_MAP_WRITE, 0, 0, (uint)this._Size);
                if (this._Data == IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(this._SharedMemoryFile);
                    this._SharedMemoryFile = IntPtr.Zero;
                    throw new Exception("Cannot map view of file. Name: " + this._Name + ", Size:" + this._Size.ToString());
                }
            }

            /// <summary>
            /// Close shared memory
            /// </summary>
            public void Close()
            {
                if (this._SharedMemoryFile != IntPtr.Zero)
                {
                    NativeMethods.UnmapViewOfFile(this._Data);
                    this._Data = IntPtr.Zero;
                    NativeMethods.CloseHandle(this._SharedMemoryFile);
                    this._SharedMemoryFile = IntPtr.Zero;
                    this._AlreadyExist = false;
                }
            }

            public byte[] Read()
            {
                if (this._SharedMemoryFile == IntPtr.Zero)
                    throw new Exception("The SharedMemory is not created.");
                byte[] bytData = new byte[this._Size];
                Marshal.Copy(this._Data, bytData, 0, bytData.Length);
                return bytData;
            }

            public void Write(byte[] bytData)
            {
                if (this._SharedMemoryFile == IntPtr.Zero)
                    throw new Exception("The SharedMemory is not created.");
                Marshal.Copy(bytData, 0, this._Data, bytData.Length > this._Size ? this._Size : bytData.Length);
            }
        }

        #endregion

        #region class NativeMethods

        static class NativeMethods
        {
            #region Native Method

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, IntPtr lParam);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr CreateFileMapping(int hFile, IntPtr lpAttributes, uint flProtect, uint dwMaxSizeHi, uint dwMaxSizeLow, string lpName);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr OpenFileMapping(int dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, string lpName);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr MapViewOfFile(IntPtr hFileMapping, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, uint dwNumberOfBytesToMap);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern bool UnmapViewOfFile(IntPtr pvBaseAddress);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32", EntryPoint = "GetLastError")]
            public static extern int GetLastError();


            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr CreateSemaphoreW(IntPtr lpSemaphoreAttributes, int lInitialCount, int lMaximumCount, string lpName);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern uint WaitForSingleObject(IntPtr hSemaphore, uint dwMilliseconds);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto)]
            public static extern bool ReleaseSemaphore(IntPtr hSemaphore, int lReleaseCount, IntPtr lpPreviousCount);

            public const int ERROR_ALREADY_EXISTS = 183;

            public const int FILE_MAP_COPY = 0x0001;
            public const int FILE_MAP_WRITE = 0x0002;
            public const int FILE_MAP_READ = 0x0004;
            public const int FILE_MAP_ALL_ACCESS = 0x0002 | 0x0004;

            public const int PAGE_READONLY = 0x02;
            public const int PAGE_READWRITE = 0x04;
            public const int PAGE_WRITECOPY = 0x08;
            public const int PAGE_EXECUTE = 0x10;
            public const int PAGE_EXECUTE_READ = 0x20;
            public const int PAGE_EXECUTE_READWRITE = 0x40;

            public const int SEC_COMMIT = 0x8000000;
            public const int SEC_IMAGE = 0x1000000;
            public const int SEC_NOCACHE = 0x10000000;
            public const int SEC_RESERVE = 0x4000000;

            public const int INVALID_HANDLE_VALUE = -1;

            #endregion
        }

        #endregion

        #endregion
    }
}
