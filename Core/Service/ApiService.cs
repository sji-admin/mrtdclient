using cmrtd.Core.Model;
using cmrtd.Infrastructure.DeskoDevice;
using Serilog;
using System.Text;
using System.Text.Json;

namespace cmrtd.Core.Service
{
    public class ApiService
    {
        private readonly SensepassKaiSettings _settings;
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        public ApiService(SensepassKaiSettings settings)
        {
            _settings = settings;
            _http = new HttpClient { BaseAddress = new Uri(_settings.Host) };
        }

        public async Task AddMemberToGroupAsync(string nik, string imageBase64, string surname, string givenNames)
        {
            var payload = new
            {
                nik = nik,
                photo = imageBase64,
                name = $"{surname} {givenNames}",
                gender = "",
                address = "",
                birthplace = "",
                birthdate = "",
                religion = "",
                married = "",
                province = "",
                city = "",
                district = "",
                subdistrict = "",
                stationcode = "PR",
                source = "PASPOR"
            };           

            // Serialize dengan opsi custom
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [API] Start SensepassKai");

            var request = new HttpRequestMessage(HttpMethod.Post, "addMemberToGroup")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Cookie", _settings.Token);

            try
            {
                var response = await _http.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [Payload] {json}");
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [SensetimeKai] Status {response.StatusCode}, Response: {body}");
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [API] Done send to SensepassKai");

            } catch (Exception ex)
            {
                Console.WriteLine($">>> {DateTime.Now:HH:mm:ss.fff} [ERROR] >>> [SensetimeKai] Error: {ex.Message}");
            }

        }

        public async Task SendCallbackAsync(string mrz, string datapage64, string location,string imgBase64, string url, string imgformat, string faceLocation, string msgError)
        {
            var payload = new Callback
            {
                body = new Body
                {
                    code = 200,
                    err_msg = msgError,
                    data = new Data
                    {
                        mrz = mrz,
                        bcbp = "",
                        docType = "document",
                        uuid = Guid.NewGuid().ToString(),
                        valid = true,
                        rgbImage = new RgbImage
                        {
                            motionBlur = false,
                            face = null,
                            isUvDull = true,
                            isB900Ink = true,
                            location = location,
                            faceLocation = faceLocation,
                            imgBase64 = datapage64,
                            imgFaceBase64 = imgBase64,
                            imgFormat = imgformat
                        }
                    }
                },
                statusCode = "OK",
                statusCodeValue = 200
            };

            using HttpClient client = new HttpClient();
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(url, content);
                var result = await response.Content.ReadAsStringAsync();

                Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [CALLBACK] Status: {response.StatusCode}");
                Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [CALLBACK] Response: {result}");
                Log.Information($">>> {DateTime.Now:HH:mm:ss.fff} [INFO] >>> [API] Done Callback");
            }
            catch (Exception ex)
            {
                Log.Information($">>> [CALLBACK] Error: {ex.Message}");
            }
        }

        
    }
}
