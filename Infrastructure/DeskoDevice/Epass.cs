using cmrtd.Core.Model;
using Desko.ePass;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace cmrtd.Infrastructure.DeskoDevice
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class Epass
    {
        //private readonly DeviceHandler _deviceHandler;
        public void ReadPassportFull(string ocr)
        {
            Console.WriteLine("Read passport full:");

            Console.Write($" MRZ/CAN: {ocr}");
            string key = ocr;

            Console.WriteLine("Perform...");

            try
            {
                using (Processor proc = new Processor())
                {
                    //proc.Perform(PerformScenario.MRTD_DL_FULL, PerformOptionType.ImgConversion, key);

                    Console.WriteLine("Result:");
                    Console.WriteLine(proc.prettyJSON);

                    //if (proc.Authentications != null)
                    //{
                    //    Console.WriteLine("Auth Results:");
                    //    Console.WriteLine(proc.Authentications.prettyJSON);
                    //}
                }
            }
            catch (ePassException ex)
            {
                Console.WriteLine("ePassException: " + ex.GetMessage());
            }
            catch (Exception e)
            {
                Console.WriteLine("General Exception: " + e.Message);
            }
            
        }

        public async Task ReadPassportSimpleTreeAsync(string ocr, CallbackSettings callbackSettings, Epassport epassport)
        {
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] Read passport with simple properties tree:");

            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] MRZ/CAN: {ocr}");
            string key = ocr;

            Console.WriteLine("Perform...");

            //Epassport result = new Epassport();

            try
            {
                await Task.Run(() =>
                {
                    using (SimpleProcessor proc = new SimpleProcessor())
                    {                       
                        //proc.Perform(PerformScenario.MRTD_DL_LIGHT, PerformOptionType.ImgConversion, ocr);

                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] Result:");
                        Console.WriteLine(proc.prettyJSON);

                        epassport.MRZ = proc.MRZ;
                        Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] MRZ on Chip: {epassport.MRZ}");
                        //string base64Face = null;

                        if (proc.BiometricFaceImgData != null)
                        {
                            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] BiometricFaceImgData length: {proc.BiometricFaceImgData.Length} bytes");
                            using (MemoryStream ms = new MemoryStream(proc.BiometricFaceImgData))
                            {
                                using (Image img = Image.FromStream(ms))
                                {
                                    //ImageFormat format = img.RawFormat;
                                    //epassport.ImageFormat = format.ToString();
                                    Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [DEVICE] BiometricFace Format: {epassport.ImageFormat}");
                                }
                            }

                            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ScanResult");
                            Directory.CreateDirectory(folder);

                            string jpegPath = Path.Combine(folder, $"portrait_chip_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                            File.WriteAllBytes(jpegPath, proc.BiometricFaceImgData);

                            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SCAN] Image from Chip saved directly to: {jpegPath}");
                            epassport.faceLocation = jpegPath;

                            epassport.FaceBase64 = Convert.ToBase64String(proc.BiometricFaceImgData);                            
                            Console.WriteLine($">>> [INFO] >>> [SCAN] BiometricFaceImgData Base64 length: {epassport.FaceBase64.Length}");

                        }
                        else
                        {
                            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [WARN] >>> [SCAN] BiometricFaceImgData is NULL");
                        }

                    }
                });
            }
            catch (ePassException ex)
            {
                Console.WriteLine($"ePassException: {ex.GetMessage()}");
            }
            catch (Exception e)
            {
                Console.WriteLine("General Exception: " + e.Message);
            }
        }

        public void ReadPassportNoPACE(string ocr)
        {
            Console.WriteLine("Read passport with preventing PACE:");            

            Console.Write("MRZ: ocr");
            string key = ocr;

            Console.WriteLine("Perform...");

            try
            {
                using (Processor proc = new Processor())
                {
                    //proc.Perform(PerformScenario.MRTD_DL_LIGHT, PerformOptionType.NoPACE, key);

                    Console.WriteLine("Result:");
                    Console.WriteLine(proc.prettyJSON);

                }
            }
            catch (ePassException ex)
            {
                Console.WriteLine("ePassException: " + ex.GetMessage());
            }
            catch (Exception e)
            {
                Console.WriteLine("General Exception: " + e.Message);
            }            
        }


    }
}
