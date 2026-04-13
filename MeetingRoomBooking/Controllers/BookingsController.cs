using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MeetingRoomBooking.Data;
using MeetingRoomBooking.Models;
using MeetingRoomBooking.Helpers;
using MeetingRoomBooking.DTO;

namespace MeetingRoomBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BookingsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/bookings
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetBookings()
        {
            var bookings = await _context.Bookings
                .Include(b => b.MeetingRoom)
                .Include(b => b.User)
                .ToListAsync();

            return Ok(ApiResponseHelper.Success(bookings, "Get bookings successfully"));
        }

        // GET: api/bookings/{id}
        [Authorize]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetBooking(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.MeetingRoom)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
                return NotFound(ApiResponseHelper.Fail<string>("Booking not found"));

            return Ok(ApiResponseHelper.Success(booking, "Get booking successfully"));
        }

        // Dashboard
        [Authorize]
        [HttpGet("dashboard")]
        public IActionResult GetDashboard()
        {
            var now = DateTime.Now;
            var today = DateTime.Today;

            // ===== STATS =====
            var totalRoom = _context.MeetingRooms.Count();

            var todayBookings = _context.Bookings
                .Count(b => b.StartTime.Date == today && b.Status != "Cancelled");

            var ongoingCount = _context.Bookings
                .Count(b => b.Status != "Cancelled" &&
                            b.StartTime <= now &&
                            b.EndTime >= now);

            var upcomingCount = _context.Bookings
                .Count(b => b.Status != "Cancelled" &&
                            b.StartTime > now);

            var recentCount = _context.Bookings
                .Count(b => b.Status != "Cancelled" &&
                            b.EndTime < now &&
                            b.EndTime.Date == today);

            // ===== BASE QUERY =====
            var baseQuery = _context.Bookings
                .Include(b => b.MeetingRoom)
                .Include(b => b.User)
                .Where(b => b.Status != "Cancelled");

            // ===== ONGOING =====
            var ongoingList = baseQuery
                .Where(b => b.StartTime <= now && b.EndTime >= now)
                .OrderBy(b => b.EndTime)
                .Select(b => new
                {
                    roomName = b.MeetingRoom.Name,
                    capacity = b.MeetingRoom.Capacity,
                    email = b.User.Email,
                    fullName = b.User.FullName,
                    startTime = b.StartTime,
                    endTime = b.EndTime,
                    location = b.MeetingRoom.Location
                })
                .ToList();

            // ===== UPCOMING =====
            var upcomingList = baseQuery
                .Where(b => b.StartTime > now)
                .OrderBy(b => b.StartTime)
                .Select(b => new
                {
                    roomName = b.MeetingRoom.Name,
                    capacity = b.MeetingRoom.Capacity,
                    email = b.User.Email,
                    fullName = b.User.FullName,
                    startTime = b.StartTime,
                    endTime = b.EndTime,
                    location = b.MeetingRoom.Location
                })
                .ToList();

            // ===== RECENT =====
            var recentList = baseQuery
                .Where(b => b.EndTime < now && b.EndTime.Date == today)
                .OrderByDescending(b => b.EndTime)
                .Select(b => new
                {
                    roomName = b.MeetingRoom.Name,
                    capacity = b.MeetingRoom.Capacity,
                    email = b.User.Email,
                    fullName = b.User.FullName,
                    startTime = b.StartTime,
                    endTime = b.EndTime,
                    location = b.MeetingRoom.Location
                })
                .ToList();

            return Ok(ApiResponseHelper.Success(new
            {
                stats = new
                {
                    totalRoom,
                    todayBookings,
                    ongoing = ongoingCount,
                    upcoming = upcomingCount,
                    recent = recentCount
                },
                ongoingList,
                upcomingList,
                recentList
            }, "Dashboard data loaded"));
        }

        // POST Booking
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PostBooking(CreateBookingDto dto)
        {
            using var transaction = await _context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var now = DateTime.Now;
            var today = now.Date;

            if (userId == null)
                return Unauthorized(ApiResponseHelper.Fail<string>("User not found"));

            // ===== ROOM IDS =====
            var roomIds = new List<int>();

            if (dto.MeetingRoomIds?.Any() == true)
                roomIds = dto.MeetingRoomIds;
            else if (dto.MeetingRoomId.HasValue)
                roomIds.Add(dto.MeetingRoomId.Value);
            else
                return BadRequest(ApiResponseHelper.Fail<string>("No room selected"));

            // ===== TIME =====
            var start = dto.StartTime.Kind == DateTimeKind.Utc
                ? dto.StartTime.ToLocalTime()
                : DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Local);

            var end = dto.EndTime.Kind == DateTimeKind.Utc
                ? dto.EndTime.ToLocalTime()
                : DateTime.SpecifyKind(dto.EndTime, DateTimeKind.Local);

            // ===== VALIDATE =====
            if (start < now)
                return BadRequest(ApiResponseHelper.Fail<string>("Không thể đặt phòng trong quá khứ"));

            if (start.Date < today)
                return BadRequest(ApiResponseHelper.Fail<string>("Không thể đặt ngày đã qua"));

            if (start >= end)
                return BadRequest(ApiResponseHelper.Fail<string>("EndTime phải lớn hơn StartTime"));

            // ===== GET ROOMS =====
            var rooms = await _context.MeetingRooms
                .Where(r => roomIds.Contains(r.Id))
                .ToListAsync();

            if (rooms.Count != roomIds.Count)
                return NotFound(ApiResponseHelper.Fail<string>("Room không tồn tại"));

            if (rooms.Any(r => !r.IsActive))
                return BadRequest(ApiResponseHelper.Fail<string>("Có phòng không khả dụng"));

            // ===== BUFFER =====
            var buffer = rooms.First().BufferMinutes;
            var startWithBuffer = start.AddMinutes(-buffer);
            var endWithBuffer = end.AddMinutes(buffer);

            // ===== OVERLAP =====
            var isConflict = await _context.Bookings.AnyAsync(b =>
                roomIds.Contains(b.MeetingRoomId) &&
                b.Status == "Booked" &&
                b.StartTime < endWithBuffer &&
                b.EndTime > startWithBuffer
            );

            if (isConflict)
                return BadRequest(ApiResponseHelper.Fail<string>("Trùng lịch booking"));

            // ===== CREATE =====
            var bookings = roomIds.Select(roomId => new Booking
            {
                MeetingRoomId = roomId,
                UserId = userId,
                StartTime = start,
                EndTime = end,
                Status = "Pending",
                CreatedAt = DateTime.Now
            }).ToList();

            _context.Bookings.AddRange(bookings);

            try
            {
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                return BadRequest(ApiResponseHelper.Fail<string>("Đã bị đặt trước"));
            }

            return Ok(ApiResponseHelper.Success(bookings, "Booking created successfully"));
        }

        // GET bookings by 1 room 
        [HttpGet("room")]
        [Authorize]
        public async Task<ActionResult> GetBookingsByRoomAndDate(int roomId, DateTime date, int? bookingId)
        {
            var room = await _context.MeetingRooms.FirstAsync(r => r.Id == roomId);
            var buffer = room.BufferMinutes;

            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1);

            var bookings = await _context.Bookings
                .Include(b => b.MeetingRoom)
                .Where(b => b.MeetingRoomId == roomId &&
                            b.StartTime >= startOfDay &&
                            b.StartTime < endOfDay &&
                            b.Status == "Booked" && // ✅ chỉ block booking đã duyệt
                            (!bookingId.HasValue || b.Id != bookingId)) // ✅ loại chính nó
                .Select(b => new
                {
                    StartTime = b.StartTime.AddMinutes(-buffer),
                    EndTime = b.EndTime.AddMinutes(buffer),
                    Status = b.Status,
                    Location = b.MeetingRoom.Location,
                    Capacity = b.MeetingRoom.Capacity
                })
                .ToListAsync();

            return Ok(bookings);
        }
        // get booking nhiều room 
        [HttpGet("rooms")]
        [Authorize]
        public async Task<IActionResult> GetBookingsByRooms(
    [FromQuery] List<int> roomIds,
    [FromQuery] DateTime date,
    [FromQuery] int? bookingId)
        {
            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1);

            var bookings = await _context.Bookings
                .Include(b => b.MeetingRoom)
                .Where(b => roomIds.Contains(b.MeetingRoomId) &&
                            b.StartTime >= startOfDay &&
                            b.StartTime < endOfDay &&
                            b.Status == "Booked" && // ✅ FIX
                            (!bookingId.HasValue || b.Id != bookingId)) // ✅ FIX
                .Select(b => new
                {
                    b.MeetingRoomId,
                    b.StartTime,
                    b.EndTime,
                    Buffer = b.MeetingRoom.BufferMinutes,
                    b.MeetingRoom.Location,
                    b.MeetingRoom.Capacity
                })
                .ToListAsync();

            var result = bookings.Select(b => new
            {
                b.MeetingRoomId,
                StartTime = b.StartTime.AddMinutes(-b.Buffer),
                EndTime = b.EndTime.AddMinutes(b.Buffer),
                b.Location,
                b.Capacity
            });

            return Ok(result);
        }

        // GET by date
        [HttpGet("by-date")]
        [Authorize]
        public async Task<ActionResult> GetBookingsByDate(string date, int? roomId)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
                return BadRequest("Invalid date format");

            var startOfDay = parsedDate.Date;
            var endOfDay = startOfDay.AddDays(1);

            var query = _context.Bookings
                .Where(b => b.Status != "Cancelled" &&
                            b.EndTime > startOfDay &&
                            b.StartTime < endOfDay);

            if (roomId.HasValue)
                query = query.Where(b => b.MeetingRoomId == roomId.Value);

            var bookings = await query
                .Include(b => b.MeetingRoom)
                .Select(b => new
                {
                    b.Id,
                    b.StartTime,
                    b.EndTime,
                    b.Status,
                    RoomName = b.MeetingRoom.Name,
                    Location = b.MeetingRoom.Location
                })
                .ToListAsync();

            return Ok(bookings);
        }

        // My bookings (paging)
        [HttpGet("my-bookings")]
        [Authorize]
        public async Task<IActionResult> GetMyBookings(int page = 1, int pageSize = 10)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var now = DateTime.Now;

            await _context.Bookings
                .Where(b => b.UserId == userId &&
                            b.Status != "Completed" &&
                            b.EndTime < now)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(b => b.Status, "Completed"));

            var query = _context.Bookings
                .Include(b => b.MeetingRoom)
                .Include(b => b.User)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.StartTime);

            var totalItems = await query.CountAsync();

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new
            {
                currentPage = page,
                pageSize,
                totalItems,
                totalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
                items
            });
        }

        // Cancel booking
        [HttpPut("cancel/{id}")]
        [Authorize]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
                return NotFound("Không tìm thấy booking");

            // 🔒 chỉ owner mới được huỷ
            if (booking.UserId != userId)
                return StatusCode(403, "Không có quyền");

            // ❌ không cho huỷ nếu đã completed
            if (booking.Status == "Completed")
                return BadRequest("Không thể huỷ booking đã hoàn thành");

            // ❌ nếu đã huỷ rồi
            if (booking.Status == "Cancelled")
                return BadRequest("Booking đã được huỷ trước đó");

            // ❌ chỉ cho huỷ Pending + Booked
            if (booking.Status != "Pending" && booking.Status != "Booked")
                return BadRequest("Trạng thái không hợp lệ để huỷ");

            // ⏰ 🔥 CHECK 3 TIẾNG
            var diffHours = (booking.StartTime - DateTime.Now).TotalHours;

            if (diffHours < 3)
                return BadRequest("Chỉ được huỷ trước 3 tiếng so với giờ bắt đầu");

            // ✅ hợp lệ → huỷ
            booking.Status = "Cancelled";

            await _context.SaveChangesAsync();

            return Ok(new { message = "Hủy thành công" });
        }

        // Admin update
        [HttpPut("admin/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateBooking(int id, [FromBody] UpdateBookingDto dto)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
                return NotFound(new { message = "Booking not found" });

            if (booking.Status == "Completed")
                return BadRequest(new { message = "Cannot edit completed booking" });

            if (dto.EndTime <= dto.StartTime)
                return BadRequest(new { message = "Invalid time" });

            // ===== UPDATE STATUS =====
            if (!string.IsNullOrEmpty(dto.Status))
            {
                var allowed = new[] { "Pending", "Booked", "Cancelled" };

                if (!allowed.Contains(dto.Status))
                    return BadRequest(new { message = "Invalid status" });

                // 🔥 nếu chuyển sang Booked → check conflict
                if (dto.Status == "Booked")
                {
                    var isConflict = await _context.Bookings.AnyAsync(b =>
                        b.Id != id &&
                        b.MeetingRoomId == dto.MeetingRoomId &&
                        b.Status == "Booked" &&
                        dto.StartTime < b.EndTime &&
                        dto.EndTime > b.StartTime
                    );

                    if (isConflict)
                        return BadRequest(new { message = "Time slot already booked" });
                }

                booking.Status = dto.Status;
            }

            // ===== UPDATE TIME / ROOM =====
            var isConflictTime = await _context.Bookings.AnyAsync(b =>
                b.Id != id &&
                b.MeetingRoomId == dto.MeetingRoomId &&
                b.Status == "Booked" &&
                dto.StartTime < b.EndTime &&
                dto.EndTime > b.StartTime
            );

            if (isConflictTime)
                return BadRequest(new { message = "Time slot already booked" });

            booking.MeetingRoomId = dto.MeetingRoomId;
            booking.StartTime = dto.StartTime;
            booking.EndTime = dto.EndTime;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Updated successfully" });
        }
        //User update
        [HttpPut("user/{id}")]
        [Authorize]
        public async Task<IActionResult> UserUpdateBooking(int id, UserUpdateBookingDto dto)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null)
                return NotFound(new { message = "Booking not found" });

            // 🔐 Check quyền
            if (booking.UserId != userId)
                return StatusCode(403, new { message = "No permission" });

            // ❗ Chỉ cho sửa khi Pending
            if (booking.Status != "Pending")
                return BadRequest(new { message = "Only Pending booking can be edited" });

            // ❗ Validate time
            if (dto.EndTime <= dto.StartTime)
                return BadRequest(new { message = "Invalid time" });

            // ===== CHECK CONFLICT =====
            var isConflict = await _context.Bookings.AnyAsync(b =>
                b.Id != id && // loại chính nó
                b.MeetingRoomId == dto.MeetingRoomId &&
                b.Status == "Booked" && // chỉ conflict với booking đã được duyệt
                dto.StartTime < b.EndTime &&
                dto.EndTime > b.StartTime
            );

            if (isConflict)
                return BadRequest(new { message = "Time slot already booked" });

            // ===== UPDATE =====
            booking.MeetingRoomId = dto.MeetingRoomId;
            booking.StartTime = dto.StartTime;
            booking.EndTime = dto.EndTime;

            // ❌ KHÔNG cho user đổi status
            // booking.Status = dto.Status; // không có dòng này

            await _context.SaveChangesAsync();

            return Ok(new { message = "Updated successfully" });
        }
        // DELETE
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBooking(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null)
                return NotFound(ApiResponseHelper.Fail<string>("Booking not found"));

            _context.Bookings.Remove(booking);
            await _context.SaveChangesAsync();

            return Ok(ApiResponseHelper.Success<string>(null, "Deleted successfully"));
        }
    }
}