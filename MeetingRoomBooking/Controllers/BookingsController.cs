using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MeetingRoomBooking.Data;
using MeetingRoomBooking.Models;
using MeetingRoomBooking.Helpers;
using MeetingRoomBooking.DTO;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;

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

        // GET: api/bookings/5
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

        // dashboard
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
                .Count(b => b.Status != "Cancelled"
                            && b.StartTime <= now
                            && b.EndTime >= now);

            var upcomingCount = _context.Bookings
                .Count(b => b.Status != "Cancelled"
                            && b.StartTime > now);

            var recentCount = _context.Bookings
                .Count(b => b.Status != "Cancelled"
                            && b.EndTime < now
                            && b.EndTime.Date == today);

            // ===== BASE QUERY (dùng lại) =====
            var baseQuery = _context.Bookings
                .Include(b => b.MeetingRoom)
                .Include(b => b.User)
                .Where(b => b.Status != "Cancelled");

            // ===== ONGOING LIST =====
            var ongoingList = baseQuery
                .Where(b => b.StartTime <= now && b.EndTime >= now)
                .OrderBy(b => b.EndTime)
                
                .Select(b => new
                {
                    roomName = b.MeetingRoom.Name,
                    capacity = b.MeetingRoom.Capacity,
                    email = b.User.Email,
                    startTime = b.StartTime,
                    location = b.MeetingRoom.Location,
                    endTime = b.EndTime,
                    username = b.User.UserName
                })
                .ToList();

            // ===== UPCOMING LIST =====
            var upcomingList = baseQuery
                .Where(b => b.StartTime > now)
                .OrderBy(b => b.StartTime)
                
                .Select(b => new
                {
                    roomName = b.MeetingRoom.Name,
                    startTime = b.StartTime,
                    capacity = b.MeetingRoom.Capacity,
                    location = b.MeetingRoom.Location,

                    email = b.User.Email,
                    endTime = b.EndTime,
                    username = b.User.UserName
                })
                .ToList();

            // ===== RECENT LIST (đã kết thúc hôm nay) =====
            var recentList = baseQuery
                .Where(b => b.EndTime < now && b.EndTime.Date == today)
                .OrderByDescending(b => b.EndTime)
               
                .Select(b => new
                {
                    roomName = b.MeetingRoom.Name,
                    startTime = b.StartTime,
                    capacity = b.MeetingRoom.Capacity,
                    location = b.MeetingRoom.Location,
                    email = b.User.Email,
                    endTime = b.EndTime,
                    username = b.User.UserName
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

            var start = dto.StartTime.Kind == DateTimeKind.Utc
                ? dto.StartTime.ToLocalTime()
                : DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Local);

            var end = dto.EndTime.Kind == DateTimeKind.Utc
                ? dto.EndTime.ToLocalTime()
                : DateTime.SpecifyKind(dto.EndTime, DateTimeKind.Local);

            if (start < now)
                return BadRequest(ApiResponseHelper.Fail<string>("Không thể đặt phòng trong thời gian đã qua"));

            if (start.Date < today)
                return BadRequest(ApiResponseHelper.Fail<string>("Không thể đặt phòng cho ngày đã qua"));

            if (start >= end)
                return BadRequest(ApiResponseHelper.Fail<string>("Thời gian kết thúc phải lớn hơn thời gian bắt đầu"));

            var room = await _context.MeetingRooms.FirstOrDefaultAsync(r => r.Id == dto.MeetingRoomId);
            if (room == null)
                return NotFound(ApiResponseHelper.Fail<string>("Meeting room not found"));

            if (!room.IsActive)
                return BadRequest(ApiResponseHelper.Fail<string>("Meeting room is not available"));

            var buffer = room.BufferMinutes;
            var startWithBuffer = start.AddMinutes(-buffer);
            var endWithBuffer = end.AddMinutes(buffer);

            var isConflict = await _context.Bookings.AnyAsync(b =>
                b.MeetingRoomId == dto.MeetingRoomId &&
                b.Status == "Booked" &&
                b.StartTime < endWithBuffer &&
                b.EndTime > startWithBuffer
            );

            if (isConflict)
                return BadRequest(ApiResponseHelper.Fail<string>("Phòng đã được đặt trong khoảng thời gian gần này"));

            var booking = new Booking
            {
                MeetingRoomId = dto.MeetingRoomId,
                UserId = userId,
                StartTime = start,
                EndTime = end,
                Status = "Booked",
                CreatedAt = DateTime.Now
            };

            _context.Bookings.Add(booking);

            try
            {
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                return BadRequest(ApiResponseHelper.Fail<string>("Slot đã bị người khác đặt trước"));
            }

            return Ok(ApiResponseHelper.Success(booking, "Booking created successfully"));
        }


        [HttpGet("room")]
        [Authorize]
        public async Task<ActionResult> GetBookingsByRoomAndDate(int roomId, DateTime date)
        {
            var room = await _context.MeetingRooms.FirstAsync(r => r.Id == roomId);

            var buffer = room.BufferMinutes;

            var startOfDay = date.Date;
            var endOfDay = startOfDay.AddDays(1);

            var bookings = await _context.Bookings
                .Include(b => b.MeetingRoom)
                .Where(b =>
                    b.MeetingRoomId == roomId &&
                    b.StartTime >= startOfDay &&
                    b.StartTime < endOfDay &&
                    b.Status != "Cancelled"
                    
                )
                .Select(b => new
                {
                    StartTime = b.StartTime.AddMinutes(-buffer),
                    EndTime = b.EndTime.AddMinutes(buffer),
                    Status = b.Status ,
                    Location = b.MeetingRoom.Location,
                    Capacity = b.MeetingRoom.Capacity,
                    
                    
                })
                .ToListAsync();

            return Ok(bookings);
        }
        //get booking by date
        [HttpGet("by-date")]
        [Authorize]
        public async Task<ActionResult> GetBookingsByDate(string date, int? roomId)
        {
            if (!DateTime.TryParse(date, out var parsedDate))
            {
                return BadRequest("Invalid date format");
            }

            var startOfDay = parsedDate.Date;
            var endOfDay = startOfDay.AddDays(1);

            var query = _context.Bookings
                .Where(b =>
                    b.Status != "Cancelled" &&
                    b.EndTime > startOfDay &&
                    b.StartTime < endOfDay);

            if (roomId.HasValue)
            {
                query = query.Where(b => b.MeetingRoomId == roomId.Value);
            }

            var bookings = await query
                .Include(b => b.MeetingRoom)
                .Select(b => new
                {
                    b.Id,
                    b.StartTime,
                    b.EndTime,
                    b.Status,
                    RoomName = b.MeetingRoom.Name,

                    Location = b.MeetingRoom.Location,
                })
                .ToListAsync();

            return Ok(bookings);
        }

        [HttpGet("my-bookings")]
        [Authorize]
        public async Task<IActionResult> GetMyBookings(int page = 1, int pageSize = 10)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (userId == null) return Unauthorized();

            var now = DateTime.Now;

            // ✅ update nhanh, không load RAM
            await _context.Bookings
           .Where(b => b.UserId == userId
         && b.Status != "Completed"
         && b.EndTime < now)
     .ExecuteUpdateAsync(setters => setters
         .SetProperty(b => b.Status, "Completed"));

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


        //hủy booking 
        [HttpPut("cancel/{id}")]
        [Authorize]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null)
                return NotFound("Không tìm thấy booking");

            if (booking.UserId != userId)
                return StatusCode(403, "Bạn không có quyền hủy");

            booking.Status = "Cancelled";

            await _context.SaveChangesAsync();

            return Ok(new { message = "Hủy thành công" });
        }

        // update booking 
        [HttpPut("admin/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateBooking(int id, [FromBody] UpdateBookingDto dto)
        {
            var booking = await _context.Bookings.FindAsync(id);

            if (booking == null)
                return NotFound(new { message = "Booking not found" });

            // ❌ không cho sửa booking đã hoàn thành
            if (booking.Status == "Completed")
                return BadRequest(new { message = "Cannot edit completed booking" });

            // ❌ validate thời gian
            if (dto.EndTime <= dto.StartTime)
                return BadRequest(new { message = "EndTime must be greater than StartTime" });
            if (!string.IsNullOrEmpty(dto.Status))
            {
                var allowedStatuses = new[] { "Booked", "Cancelled" };

                if (!allowedStatuses.Contains(dto.Status))
                    return BadRequest(new { message = "Invalid status" });

                booking.Status = dto.Status;
            }

            // ❌ check trùng lịch
            var isConflict = await _context.Bookings.AnyAsync(b =>
                b.Id != id &&
                b.MeetingRoomId == dto.MeetingRoomId &&
                b.Status != "Cancelled" &&
                (
                    dto.StartTime < b.EndTime &&
                    dto.EndTime > b.StartTime
                ));

            if (isConflict)
                return BadRequest(new { message = "Time slot already booked" });

            // ✅ update
            booking.MeetingRoomId = dto.MeetingRoomId;
            booking.StartTime = dto.StartTime;
            booking.EndTime = dto.EndTime;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Booking updated successfully"
            });
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

            return Ok(ApiResponseHelper.Success<string>(null, "Booking deleted successfully"));
        }
    }
}