using cmrtd.Core.Model;
using Desko.ePass;
using Desko.EPass;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Serilog;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace cmrtd.Infrastructure.DeskoDevice
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class UePass
    {
        private void DataGrabber(string ctx, DataGrabberInfo obj)
        {
            StringBuilder printout = new StringBuilder();
            printout.Append("[").Append(ctx).Append("] LiveData (").Append(obj.Source).Append(") - ").Append(obj.Fullname).Append(" [");
            ItemType type = obj.ValueType;
            switch (type)
            {
                case ItemType.Int: printout.Append("Int]: ").Append(obj.IntValue.ToString()); break;
                case ItemType.String: printout.Append("String]: ").Append(obj.StringValue); break;
                case ItemType.Blob: printout.Append("BLOB]: ").Append(obj.BlobValue.Length).Append(" Bytes"); break;
                default: printout.Append("Unknown Type"); break;
            }
            PrintLine(printout.ToString());
        }
        private void ValidationGrabber(string ctx, ValidationGrabberInfo obj)
        {
            StringBuilder printout = new StringBuilder();
            printout.Append("[").Append(ctx).Append("] LiveValidation - ").Append(obj.Fullname).Append(": ").Append(obj.Result);
            PrintLine(printout.ToString());
        }
        private void ProgressReporter(string ctx, PerformProgressInfo obj)
        {
            int? percent = obj.Percent;
            int halfperc = percent.Value / 2;
            StringBuilder printout = new StringBuilder();
            printout.Append("[").Append(ctx).Append("] Progress - ").Append("[").Append(obj.Sender).Append("] |");
            for (int i = 0; i < halfperc; i++) { printout.Append("#"); }
            for (int i = halfperc; i < 50; i++) { printout.Append("-"); }
            printout.Append("| ").Append(percent).Append("% - ").Append(obj.State);
            PrintLine(printout.ToString());
        }

        public void ReadPassportPentaData(string mrz, Epassport epass)
        {
            Log.Information("Read passport full");

            string key = mrz;

            using (SimpleProcessor proc = new SimpleProcessor())
            {
                proc.DoAuthentications = AuthenticationType.BAC | AuthenticationType.BAP | AuthenticationType.PACE | AuthenticationType.AutoAppSelection;
                proc.ReadMRTDFiles = MRTDFileFlag.DG01 | MRTDFileFlag.DG02;

                Log.Information("Perform...");

                bool result = proc.Perform(PerformScenario.MRTD_DL, PerformOptionType.ImgConversion, key);

                Log.Information($"  [CHIP] Chip {result}.");

                HandleMRTDResult(result, proc, epass);
            }
        }

        public void ReadPassportCkiData(string mrz, Epassport epass)
        {
            try 
            {
                Log.Information("Read passport full");

                string key = mrz;

                using (SimpleProcessor proc = new SimpleProcessor())
                {
                    proc.DoAuthentications = AuthenticationType.BAC | AuthenticationType.BAP | AuthenticationType.PACE | AuthenticationType.AutoAppSelection;
                    proc.ReadMRTDFiles = MRTDFileFlag.DG01 | MRTDFileFlag.DG02;

                    Log.Information("Perform...");

                    bool result = proc.Perform(PerformScenario.MRTD_DL, PerformOptionType.ImgConversion, key);

                    Log.Information($"  [CHIP] Chip {result}.");

                    HandleMRTDResult(result, proc, epass);
                }
            } 
            catch (Exception ex)
            {              
                Log.Information($" [ERROR] >>> {ex.Message}");
            }
            
        }

        public async Task<bool> ValidateChipAsync(string mrz)
        {
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [CHECK] Coba akses chip (UID scenario)...");

            Result chipResult = Result.Undefined;

            try
            {
                var chipTask = Task.Run(() =>
                {
                    using (var simple = new Processor())
                    {
                        bool result = simple.Perform(PerformScenario.Default, PerformOptionType.None, mrz);
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  [CHIP] Debug {result}");
                        chipResult = simple.PerformResult;
                        return result && chipResult == Result.Succeeded;
                    }
                });

                var completedTask = await Task.WhenAny(chipTask, Task.Delay(1500));

                if (completedTask == chipTask && chipTask.Result)
                {
                    Console.WriteLine("[CHECK] Chip terdeteksi!");
                    return true;
                }

                Console.WriteLine("[CHECK] Tidak ada chip atau waktu habis.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Gagal validasi chip: {ex.Message}");
                return false;
            }
        }

        public void HandleMRTDResult(bool result, Processor proc)
        {
            if (result)
            {

                PrintLine("----------------------------------------------------");
                PrintLine("Result:");
                // PrintLine(proc.prettyJSON);
                PrintLine("----------------------------------------------------");

                var bioFace = proc.BiometricFace;
                if (bioFace != null)
                {
                    System.Drawing.Image faceimg = null;
                    try
                    {
                        faceimg = bioFace.ConvertedImage;
                        if (faceimg != null)
                        {
                            PrintLine("BiometricFace image has width=", faceimg.Width, " and height=", faceimg.Height);

                            using var ms = new MemoryStream(64 * 1024); // preallocate buffer (lebih efisien)
                            faceimg.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                            //epass.FaceBase64 = Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);
                        }
                        else
                        {
                            PrintLine("BiometricFace image is null");
                            //return;
                        }
                    }
                    catch (Exception ex)
                    {
                        PrintLine("Error processing BiometricFace image: ", ex.Message);
                    }
                    finally
                    {
                        faceimg.Dispose();
                    }
                }
                else
                {
                    PrintLine("No BiometricFace image found");
                }


                // Getting Portrait
                //if (proc.BiometricFace != null)
                //{
                //    using (System.Drawing.Image faceimg = proc.BiometricFace.ConvertedImage)
                //    {
                //        if (faceimg != null)
                //        {
                //            PrintLine("BiometricFace image has width=", faceimg.Width, " and height=", faceimg.Height);

                //            using var ms = new MemoryStream(64 * 1024); // preallocate buffer (lebih efisien)
                //            faceimg.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                //            epass.FaceBase64 = Convert.ToBase64String(ms.ToArray());
                //        }
                //        else
                //        {
                //            PrintLine("BiometricFace image is null");
                //            return;
                //        }
                //    } // <-- otomatis Dispose() di sini
                //}
                //else
                //{
                //    PrintLine("No BiometricFace image found");
                //}

                //Getting MRZ
                var mrzdata = proc.MRZData;
                if (mrzdata != null)
                {
                    string mrz = mrzdata.MRZ;
                    PrintLine("Chip MRZ: ", mrz);
                }
                else
                {
                    PrintLine("No MRZ found");
                }

                var session = proc.Session;
                if (session != null)
                {
                    string usedkey = session.UsedKey;
                    PrintLine("UsedKey: ", usedkey);
                }

                ValidationInfo validations = proc.Validations;
                if (validations != null)
                {
                    PrintLine("Validations:");
                    PrintLine(" Chip is present: ", validations.ChipPresent);
                    PrintLine(" Chip is not cloned: ", validations.ChipAuthenticity);
                }

                PrintLine("----------------------------------------------------");
            }
            else
            {
                _HandlePerformFailed(proc.PerformResult, "MRTD or DL");
            }

        }

        public void HandleMRTDResult(bool result, SimpleProcessor proc, Epassport epass)
        {
            if (result)
            {
                PrintLine("----------------------------------------------------");
                PrintLine("Result:");
                //PrintLine(proc.prettyJSON);
                PrintLine("----------------------------------------------------");

                var bioFace = proc.BiometricFaceImageConverted;
                if (bioFace != null)
                {
                    System.Drawing.Image faceimg = null;
                    try
                    {
                        faceimg = bioFace;
                        if (faceimg != null)
                        {
                            Log.Information($"BiometricFace image has width= {faceimg.Width} and height= {faceimg.Height}");

                            using var ms = new MemoryStream(64 * 1024); // preallocate buffer (lebih efisien)
                            faceimg.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                            epass.FaceBase64 = Convert.ToBase64String(ms.GetBuffer(), 0, (int)ms.Length);

                            // Simpan ke Desktop\ScanResult
                            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                            string folder = Path.Combine(desktop, "ScanResult");

                            if (!Directory.Exists(folder))
                                Directory.CreateDirectory(folder);

                            string fileName = $"face.jpeg";
                            string filePath = Path.Combine(folder, fileName);

                            if (File.Exists(filePath))
                            {
                                try
                                {
                                    File.Delete(filePath);
                                    Log.Information("Existing face.jpeg deleted.");
                                }
                                catch (Exception ex)
                                {
                                    Log.Information($"Failed to delete existing face.jpeg: {ex.Message}");
                                }
                            }

                            faceimg.Save(filePath, System.Drawing.Imaging.ImageFormat.Jpeg);

                            Log.Information($"Face image saved to: {filePath}");
                        }
                        else
                        {
                            Log.Information("BiometricFace image is null");
                            //return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Information($"Error processing BiometricFace image: {ex.Message}");
                    }
                    finally
                    {
                        faceimg.Dispose();
                    }
                }
                else
                {
                    Log.Information("No BiometricFace image found");
                }

                // Info tambahan
                string usedKey = proc.UsedKey;
                if (!string.IsNullOrEmpty(usedKey))
                    Log.Information($"UsedKey: {usedKey}");

                string mrz = proc.MRZ;
                if (!string.IsNullOrEmpty(mrz))
                    Log.Information($"MRZ on Chip: {mrz}");

                if (proc.BiometricFaceImgConverted != null)
                    Log.Information($"BiometricFaceImgConverted has {proc.BiometricFaceImgConverted.Length} bytes");

                Log.Information("----------------------------------------------------");
            }
            else
            {
                _HandlePerformFailed(proc.PerformResult, "MRTD or DL");
            }
        }

        public void _Print(object str)
        {
            if (str is string)
            {
                Console.Write((string)str);
            }
            else if (str is int)
            {
                Console.Write((int)str);
            }
            else if (str is bool)
            {
                Console.Write((bool)str);
            }
            else if (str is ImageFormat)
            {
                Console.Write((ImageFormat)str);
            }
            else if (str is ValidationResult)
            {
                Console.Write((ValidationResult)str);
            }
            else if (str is Bitrate)
            {
                Bitrate bitrate = (Bitrate)str;
                switch (bitrate)
                {
                    case Bitrate.B106: Console.Write("106"); break;
                    case Bitrate.B212: Console.Write("212"); break;
                    case Bitrate.B424: Console.Write("424"); break;
                    case Bitrate.B848: Console.Write("848"); break;
                }
            }
            else if (str is IntPtr)
            {
                Console.Write((IntPtr)str);
            }
            else
            {
                Console.Write(str.ToString());
            }
        }

        public void Print(params object[] strs)
        {
            foreach (object obj in strs)
            {
                if (obj is object[])
                {
                    foreach (object obj2 in (object[])obj)
                    {
                        _Print(obj2);
                    }
                }
                else
                {
                    _Print(obj);
                }
            }
        }

        public void PrintLine(params object[] sstr)
        {
            Print(sstr);
            Console.WriteLine();
        }

        public string ReadString(string text = null)
        {
            if (!string.IsNullOrEmpty(text)) Console.Write(text);
            string readtext = Console.ReadLine();

            return readtext;
        }

        void _HandlePerformFailed(Result res, string appname)
        {
            switch (res)
            {
                case Result.NoValidLicense:
                    Print("No valid license found!\n");
                    break;

                case Result.NoCardFound:
                    Print($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  No Chip on passport found!\n");
                    break;

                case Result.SelectApplicationFailed:
                    Print("No ", appname, " application found!\n");
                    break;

                case Result.Rejected:
                    Print("No valid access!\n");
                    break;

                case Result.CardErrorCommunicationFailed:
                    Print("Communication failure!\n");
                    break;

                case Result.Failed:
                    Print($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>>  General failure occured!\n");
                    break;

                default:
                    Print("Unknown: ", (int)res);
                    break;
            }
        }


    }
}
