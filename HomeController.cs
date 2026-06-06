using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using mYPMS.Data;
using mYPMS.Models;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Newtonsoft.Json;

namespace mYPMS.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly mYPMSContext            _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SatpaClient             _satpa;

        private static readonly int _queue_size = 13;
        private readonly IWebHostEnvironment _environment;
        private static string _path     = "";
        private static string _tempPath = "";

        public HomeController(
            ILogger<HomeController> logger,
            mYPMSContext context,
            IWebHostEnvironment environment,
            UserManager<IdentityUser> userManager,
            SatpaClient satpa)
        {
            _logger      = logger;
            _context     = context;
            _userManager = userManager;
            _environment = environment;
            _satpa       = satpa;

            _path     = $@"{_environment.WebRootPath}\ParkingImages\{DateTime.Now.ToShortDateString().Replace('/', '-')}\";
            _tempPath = $@"{_environment.WebRootPath}\temp\";

            try
            {
                if (!Directory.Exists(_path))     Directory.CreateDirectory(_path);
                if (!Directory.Exists(_tempPath)) Directory.CreateDirectory(_tempPath);

                foreach (var file in Directory.GetFiles(_tempPath))
                {
                    var fi = new FileInfo(file);
                    if (fi.LastAccessTime < DateTime.Now.AddHours(-6)) fi.Delete();
                }
            }
            catch (Exception e) { _logger.LogWarning(e, "Startup directory init failed"); }
        }

        private async Task<AlprResult> CaptureImageAsync(
            string id,
            tGate? gate,
            bool isDeparture = false,
            bool isTemporary = false)
        {
            if (gate == null)
                return new AlprResult { Success = false, ErrorMessage = "GATE NULL" };

            string? url = isDeparture ? gate.xImageOutUrl : gate.xImageInUrl;
            if (string.IsNullOrEmpty(url))
                return new AlprResult { Success = false, ErrorMessage = "URL NOT SET" };

            Uri    uri            = new(url);
            var    userInfo       = uri.UserInfo.Split(':');
            bool   hasCredentials = userInfo[0] != "";
            string filename       = (isDeparture ? "ex-" : "en-") + id + ".png";
            string savePath       = isTemporary ? _tempPath : _path;
            string fullPath       = Path.Combine(savePath, filename);

            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                };
                if (hasCredentials)
                    handler.Credentials = new NetworkCredential(userInfo[0], userInfo[1]);

                using var cameraClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                using var response     = await cameraClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                if (imageBytes.Length < 1024)
                {
                    _logger.LogWarning("Camera image too small ({Bytes} bytes)", imageBytes.Length);
                    return new AlprResult { Success = false, ErrorMessage = "IMAGE TOO SMALL" };
                }

                await System.IO.File.WriteAllBytesAsync(fullPath, imageBytes);

                var result = await _satpa.RecognizeAsync(imageBytes, filename);
                if (!result.Success)
                    _logger.LogWarning("ALPR failed: {Msg}", result.ErrorMessage);
                else
                    _logger.LogInformation("ALPR OK: {Result}", result);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CaptureImageAsync failed for gate {GateId}", gate.xId);
                return new AlprResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private static string PlateOrError(AlprResult result) =>
            result.Success ? result.Plate : $"E:{result.ErrorMessage}";

        public IActionResult Welcome(string pid = "", string gid = "", int direction = 0)
        {
            if (Guid.TryParse(pid, out _) && Guid.TryParse(gid, out _)
                && direction > 0 && direction <= 3)
            {
                try
                {
                    HttpContext.Session.SetString("pid",   pid);
                    HttpContext.Session.SetString("gid",   gid);
                    HttpContext.Session.SetString("pname", _context.tParkings.Single(b => b.xId == Guid.Parse(pid)).xTitle);
                    HttpContext.Session.SetString("gname", _context.tGates.Single(b => b.xId == Guid.Parse(gid)).xName);
                    HttpContext.Session.SetInt32("dir",    direction);
                    return Redirect("~/");
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Welcome session init failed"); }
            }
            return View();
        }

        public IActionResult Index()
        {
            string? pid = HttpContext.Session.GetString("pid");
            string? gid = HttpContext.Session.GetString("gid");
            Int32?  dir = HttpContext.Session.GetInt32("dir");

            if (pid == null || gid == null || dir == null)
                return Redirect("~/Home/Welcome");

            tGate? gate = _context.tGates.SingleOrDefault(b => b.xId == Guid.Parse(gid));
            if (gate != null)
            {
                Uri? uriIn  = null;
                Uri? uriOut = null;
                try { uriIn  = new(gate.xVideoInUrl);  } catch { }
                try { uriOut = new(gate.xVideoOutUrl); } catch { }

                var userInfoIn  = uriIn  == null ? new[] { "", "" } : uriIn.UserInfo.Split(':');
                var userInfoOut = uriOut == null ? new[] { "", "" } : uriOut.UserInfo.Split(':');

                TempData["VideoSettings"] = JsonConvert.SerializeObject(new
                {
                    videoInPath    = uriIn  == null ? "" : uriIn.AbsolutePath,
                    camInHost      = uriIn  == null ? "" : uriIn.Host,
                    camInUsername  = userInfoIn.Length  > 1 ? userInfoIn[0]  : "",
                    camInPassword  = userInfoIn.Length  > 1 ? userInfoIn[1]  : "",
                    videoOutPath   = uriOut == null ? "" : uriOut.AbsolutePath,
                    camOutHost     = uriOut == null ? "" : uriOut.Host,
                    camOutUsername = userInfoOut.Length > 1 ? userInfoOut[0] : "",
                    camOutPassword = userInfoOut.Length > 1 ? userInfoOut[1] : "",
                });
            }
            return View();
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() =>
            View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

        public async Task<IStatusCodeActionResult> Traffic(
            [Bind("xId,xTagId,xLicencePlateEn,xLicencePlateEx")] tTraffic? traffic,
            string id = "")
        {
            Guid pid = new(HttpContext.Session.GetString("pid") ?? Guid.Empty.ToString());
            Guid gid = new(HttpContext.Session.GetString("gid") ?? Guid.Empty.ToString());

            tParking? parking = _context.tParkings.Include(b => b.xTariff).SingleOrDefault(b => b.xId == pid);
            if (parking == null) return Problem("پارکینگ یافت نشد");

            tGate? gate = _context.tGates.SingleOrDefault(b => b.xId == gid);
            if (gate == null) return Problem("دروازه یافت نشد");

            traffic ??= new();
            traffic.xTagId ??= id;

            tTariff? tariff = GetTariff(traffic.xTagId, ref parking);
            if (tariff == null) return Problem("تعرفه یافت نشد");

            IdentityUser user   = await _userManager.GetUserAsync(HttpContext.User);
            List<tTraffic> queue = new();

            string? queueJson = HttpContext.Session.GetString("queue");
            if (queueJson != null)
                queue = JsonConvert.DeserializeObject<List<tTraffic>>(queueJson) ?? new();

            if (!string.IsNullOrEmpty(traffic.xTagId))
            {
                tTraffic? t = _context.tTraffics
                    .Where(r => r.xTagId == traffic.xTagId && r.xStatusCode == 1)
                    .Include(b => b.xTariff)
                    .FirstOrDefault();

                if (t == null)
                {
                    if (!Guid.TryParse(traffic.xId.ToString(), out _) || traffic.xId == Guid.Empty)
                        traffic.xId = Guid.NewGuid();

                    traffic.xParkingId     = parking.xId;
                    traffic.xEntryGateId   = gate.xId;
                    traffic.xEntryDateTime = DateTime.Now;
                    traffic.xEntryOperator = user.UserName;
                    traffic.xStatusCode    = 1;

                    if (!TryMoveTempImage("en", traffic.xId, _tempPath, _path))
                    {
                        var r = await CaptureImageAsync(traffic.xId.ToString(), gate, isDeparture: false);
                        traffic.xLicencePlateEn ??= PlateOrError(r);
                    }

                    traffic.xTariff = tariff;
                }
                else
                {
                    string? exitPlate = traffic.xLicencePlateEx;
                    traffic = t;
                    traffic.xLicencePlateEx   ??= exitPlate;
                    traffic.xDepartureGateId    = gate.xId;
                    traffic.xDepartureOperator  = user.UserName;
                    traffic.xDepartureDateTime  = DateTime.Now;
                    traffic.xStatusCode         = 2;

                    if (!TryMoveTempImage("ex", traffic.xId, _tempPath, _path))
                    {
                        var r = await CaptureImageAsync(traffic.xId.ToString(), gate, isDeparture: true);
                        traffic.xLicencePlateEx ??= PlateOrError(r);
                    }
                }

                GetPrice(ref traffic);
                if (t == null) _context.tTraffics.Add(traffic);
                else           _context.tTraffics.Update(traffic);
                _context.SaveChanges();

                var inx = queue.FindIndex(x => x.xId == traffic.xId);
                if (inx != -1) queue.RemoveAt(inx);
                queue.Add(traffic);
                if (queue.Count > _queue_size)
                    queue.RemoveRange(0, queue.Count - _queue_size);

                HttpContext.Session.SetString("queue", JsonConvert.SerializeObject(
                    queue, Formatting.None,
                    new JsonSerializerSettings
                    {
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        MaxDepth = 1,
                    }));
            }

            return Ok(queue);
        }

        public async Task<IStatusCodeActionResult> GetTraffic(string? tag)
        {
            try
            {
                Guid pid = new(HttpContext.Session.GetString("pid") ?? Guid.Empty.ToString());
                Guid gid = new(HttpContext.Session.GetString("gid") ?? Guid.Empty.ToString());

                if (tag == null || pid == Guid.Empty || gid == Guid.Empty)
                    return Problem("پارکینگ و دروازه را مجدد انتخاب کنید");

                tParking? parking = _context.tParkings.Include(b => b.xTariff).SingleOrDefault(b => b.xId == pid);
                if (parking == null) return Problem("پارکینگ مربوطه یافت نشد");

                tGate? gate = _context.tGates.SingleOrDefault(b => b.xId == gid);
                if (gate == null) return Problem("دروازه مربوطه یافت نشد");

                tTariff? tariff = GetTariff(tag, ref parking);
                if (tariff == null) return Problem("تعرفه مربوطه یافت نشد");

                tTraffic? traffic = _context.tTraffics.SingleOrDefault(b => b.xTagId == tag && b.xStatusCode == 1);
                traffic ??= new();
                traffic.xTariff  = tariff;
                traffic.xParking = parking;
                traffic.xTagId ??= tag;

                if (traffic.xEntryDateTime == null)
                    traffic.xEntryDateTime = DateTime.Now;
                else
                    traffic.xDepartureDateTime ??= DateTime.Now;

                if (traffic.xStatusCode == null)
                {
                    traffic.xId         = Guid.NewGuid();
                    traffic.xStatusCode = 1;
                }
                else if (traffic.xStatusCode == 1)
                    traffic.xStatusCode = 2;

                GetPrice(ref traffic);

                bool isDeparture = traffic.xStatusCode == 2;
                var  alprResult  = await CaptureImageAsync(
                    traffic.xId.ToString(), gate, isDeparture, isTemporary: true);

                if (isDeparture) traffic.xLicencePlateEx = PlateOrError(alprResult);
                else             traffic.xLicencePlateEn = PlateOrError(alprResult);

                return Ok(traffic);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "GetTraffic failed for tag={Tag}", tag);
                return Problem(e.Message);
            }
        }

        public IStatusCodeActionResult DeleteTraffic(string? id)
        {
            if (!Guid.TryParse(id, out Guid guid)) return Problem("شناسه تردد معتبر نیست");
            tTraffic? traffic = _context.tTraffics.SingleOrDefault(b => b.xId == guid);
            if (traffic == null) return Problem("شناسه تردد یافت نشد");
            try
            {
                traffic.xStatusCode = 4;
                _context.Update(traffic);
                _context.SaveChanges();
                return Ok(JsonConvert.SerializeObject(new { message = "تردد با موفقیت برای حذف نشانه گذاری شد" }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteTraffic failed for id={Id}", id);
                return Problem("خطا در نشانه گذاری حذف<br/>" + ex.Message);
            }
        }

        public JsonResult GetParkingWithGates() =>
            Json(_context.tParkings.Include(t => t.xGates));

        public JsonResult GetInquiry(string? id)
        {
            if (id == null) return new JsonResult("شماره کارت نامعتبر است");
            tTraffic? t = _context.tTraffics.SingleOrDefault(b => b.xTagId == id && b.xStatusCode == 1);
            return new JsonResult(t);
        }

        public async Task<JsonResult> Licence()
        {
            bool alive = await _satpa.IsAliveAsync();

            return Json(new
            {
                Assembly     = "mYPMS.exe",
                DLL          = "FastAPI (Iranian ALPR)",
                Version      = "1.0.0.011013",
                OwnerCompany = "megafa Yakand",
                OwnerName    = "megafa Yakand",
                Country      = 98,
                Camera       = 1,
                StartDate    = 14000101,
                EndDate      = 14101201,
                AnprName     = "Iranian ALPR",
                AnprVersion  = "1.0.0",
                ApiStatus    = alive ? "online" : "offline",
            });
        }

        private static bool TryMoveTempImage(string prefix, Guid id, string tempPath, string destPath)
        {
            try
            {
                string srcMain  = Path.Combine(tempPath, $"{prefix}-{id}.png");
                string destMain = Path.Combine(destPath,  $"{prefix}-{id}.png");
                string srcCrop  = Path.Combine(tempPath, $"p-{prefix}-{id}.png");
                string destCrop = Path.Combine(destPath,  $"p-{prefix}-{id}.png");

                if (!System.IO.File.Exists(srcMain)) return false;
                if (System.IO.File.Exists(destMain)) System.IO.File.Delete(destMain);
                System.IO.File.Move(srcMain, destMain);

                if (System.IO.File.Exists(srcCrop))
                {
                    if (System.IO.File.Exists(destCrop)) System.IO.File.Delete(destCrop);
                    System.IO.File.Move(srcCrop, destCrop);
                }
                return true;
            }
            catch { return false; }
        }

        private tTariff? GetTariff(string? tag, ref tParking parking)
        {
            tTagList? tagFromList = _context.tTagList.SingleOrDefault(b => b.xId == tag);
            tTariff? tariff = null;
            if (tagFromList != null)
                tariff = _context.tTagList
                    .Where(b => b.xId == tag)
                    .Include(b => b.xTagGroup)
                    .ThenInclude(b => b.xTariff)
                    .First().xTagGroup.xTariff;
            tariff ??= parking.xTariff;
            return tariff;
        }

        private static void GetPrice(ref tTraffic t)
        {
            tTariff tariff          = t.xTariff;
            bool    entrancePayment = IsEntrancePayment(ref tariff);

            if (t.xStatusCode == 1 && entrancePayment)
            {
                t.xPaid = tariff.xEntryPrice;
                return;
            }

            if (t.xStatusCode == 2 && !entrancePayment)
            {
                if (t.xDepartureDateTime == null || t.xEntryDateTime == null) return;

                double parked_total_minutes;
                try { parked_total_minutes = t.xDepartureDateTime.Value.Subtract(t.xEntryDateTime.Value).TotalMinutes; }
                catch { return; }

                const int ONE_DAY_MINUTES  = 1440;
                const int ONE_HOUR_MINUTES = 60;

                int WHOLE_DAY_PRICE     = tariff.xWholeDayPrice   ?? 0;
                int ENTRANCE_PRICE      = tariff.xEntryPrice      ?? 0;
                int NORMAL_START_MINUTE = (tariff.xNightStartHour ?? 0) * 60;
                int NORMAL_END_MINUTE   = (tariff.xNightEndHour   ?? 0) * 60;
                int NORMAL_HOUR_PRICE   = tariff.xDayHourPrice    ?? 0;
                int NIGHT_HOUR_PRICE    = tariff.xNightHourPrice  ?? 0;

                int entry_minute = t.xEntryDateTime.Value.Hour     * ONE_HOUR_MINUTES + t.xEntryDateTime.Value.Minute;
                int exit_minute  = t.xDepartureDateTime.Value.Hour * ONE_HOUR_MINUTES + t.xDepartureDateTime.Value.Minute;

                int  whole_day           = (int)Math.Floor(parked_total_minutes / ONE_DAY_MINUTES);
                bool enter_exit_same_day = whole_day == 0 && t.xEntryDateTime.Value.Hour <= t.xDepartureDateTime.Value.Hour;
                bool enter_in_night;
                int  normal_minutes, night_minutes;

                if (enter_exit_same_day || exit_minute >= entry_minute)
                {
                    night_minutes  = entry_minute < NORMAL_START_MINUTE ? NORMAL_START_MINUTE - entry_minute : 0;
                    night_minutes  = exit_minute  < NORMAL_START_MINUTE ? exit_minute - entry_minute : night_minutes;
                    night_minutes  = entry_minute > NORMAL_END_MINUTE   ? exit_minute - entry_minute : night_minutes;
                    enter_in_night = night_minutes > 0;
                    normal_minutes = (int)(parked_total_minutes % ONE_DAY_MINUTES) - night_minutes;
                }
                else
                {
                    normal_minutes  = entry_minute < NORMAL_END_MINUTE   ? NORMAL_END_MINUTE   - entry_minute : 0;
                    normal_minutes -= entry_minute < NORMAL_START_MINUTE ? NORMAL_START_MINUTE - entry_minute : 0;
                    enter_in_night  = normal_minutes <= 0;
                    normal_minutes += exit_minute > NORMAL_START_MINUTE ? exit_minute - NORMAL_START_MINUTE : 0;
                    normal_minutes += exit_minute > NORMAL_END_MINUTE   ? exit_minute - NORMAL_END_MINUTE   : 0;
                    night_minutes   = (int)(parked_total_minutes % ONE_DAY_MINUTES) - normal_minutes;
                }

                if (parked_total_minutes >= ONE_HOUR_MINUTES && enter_in_night)
                {
                    if (night_minutes >= ONE_HOUR_MINUTES) night_minutes -= ONE_HOUR_MINUTES;
                    else { normal_minutes -= (ONE_HOUR_MINUTES - night_minutes); night_minutes = 0; }
                }
                else if (parked_total_minutes >= ONE_HOUR_MINUTES && !enter_in_night)
                    normal_minutes -= ONE_HOUR_MINUTES;

                int normal_hours = normal_minutes / 60;
                int night_hours  = night_minutes  / 60;
                if (normal_minutes % 60 > 0 && parked_total_minutes >= 60) normal_hours++;
                if (night_minutes  % 60 > 0 && parked_total_minutes >= 60) night_hours++;

                t.xPaid = whole_day == 0
                    ? night_hours * NIGHT_HOUR_PRICE + normal_hours * NORMAL_HOUR_PRICE + ENTRANCE_PRICE
                    : night_hours * NIGHT_HOUR_PRICE + normal_hours * NORMAL_HOUR_PRICE + whole_day * WHOLE_DAY_PRICE;
            }
        }

        private static bool IsEntrancePayment(ref tTariff t) =>
            t.xDayHourPrice == 0 || t.xWholeDayPrice == 0 || t.xNightHourPrice == 0;
    }
}
