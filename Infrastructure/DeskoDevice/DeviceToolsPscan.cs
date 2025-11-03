using Desko.FullPage;
using System.Diagnostics;
using System.Text;

namespace cmrtd.Infrastructure.DeskoDevice
{
    public class DeviceToolsPscan
    {
        /// Event arguments representing an API operation result.
        public class EventArgsOperationResult : EventArgs
        {
            public PsaException Result;
            public long ElapsedMilliseconds;
        }

        /// Event handlers to be called on API operation result. 
        public static event EventHandler<EventArgsOperationResult> OnOperationResultAvailable;

        /// Delegate type for a save API call        
        public delegate void SaveApiCall();


        /// Perform several API calls and provide default handling of exceptions.
        /// <param name = "op" > Operation with API calls.</param>
        public static void HandleApiExceptions(SaveApiCall op)
        {
            Stopwatch watch = new Stopwatch();

            watch.Start();

            EventArgsOperationResult eventArgs = new EventArgsOperationResult();
            try
            {
                op();
                eventArgs.Result = new PsaException(Result.Success, "");
            }
            catch (PsaException ex)
            {
                eventArgs.Result = ex;
            }
            catch (Desko.Parse.ParseException ex)
            {
                eventArgs.Result = new PsaException(Result.Fail, "MRZ error (" + ex.Result.ToString() + "):" + ex.Message);
            }

            watch.Stop();
            eventArgs.ElapsedMilliseconds = watch.ElapsedMilliseconds;
            if (OnOperationResultAvailable != null)
            {
                OnOperationResultAvailable(null, eventArgs);
            }
        }


        /// Convert byte array to string by masking non-ASCII characters.
        /// <param name="data">Input byte array.</param>
        /// <returns>Masked string.</returns>
        static public string MaskNonAscii(byte[] data)
        {
            StringBuilder res = new StringBuilder();

            foreach (byte b in data)
            {
                if (b >= (byte)' ' && b <= (byte)'~' && b != (byte)'{')
                {
                    res.Append((char)b);
                }
                else
                {
                    res.Append(string.Format("[{0:X2}]", b));
                }
            }
            return res.ToString();
        }
    }
}
