using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MeetingRoomBooking.Data;
using MeetingRoomBooking.Models;
using MeetingRoomBooking.Helpers;

namespace MeetingRoomBooking.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MeetingRoomsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MeetingRoomsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/meetingrooms
        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            var rooms = await _context.MeetingRooms.ToListAsync();

            return Ok(ApiResponseHelper.Success(rooms, "Get meeting rooms successfully"));
        }

        // GET: api/meetingrooms/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetRoom(int id)
        {
            var room = await _context.MeetingRooms.FindAsync(id);

            if (room == null)
                return NotFound(ApiResponseHelper.Fail<string>("Meeting room not found"));

            return Ok(ApiResponseHelper.Success(room, "Get meeting room successfully"));
        }

        // POST: api/meetingrooms
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> CreateRoom(MeetingRoom room)
        {
            _context.MeetingRooms.Add(room);
            await _context.SaveChangesAsync();

            return Ok(ApiResponseHelper.Success(room, "Meeting room created successfully"));
        }

        // PUT: api/meetingrooms/5
        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRoom(int id, MeetingRoom room)
        {
            if (id != room.Id)
                return BadRequest(ApiResponseHelper.Fail<string>("Room ID mismatch"));

            var existingRoom = await _context.MeetingRooms.FindAsync(id);

            if (existingRoom == null)
                return NotFound(ApiResponseHelper.Fail<string>("Meeting room not found"));

            existingRoom.Name = room.Name;
            existingRoom.Capacity = room.Capacity;
            existingRoom.Location = room.Location;
            existingRoom.BufferMinutes = room.BufferMinutes;
            existingRoom.IsActive = room.IsActive;

            await _context.SaveChangesAsync();

            return Ok(ApiResponseHelper.Success(existingRoom, "Meeting room updated successfully"));
        }

        // DELETE: api/meetingrooms/5
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRoom(int id)
        {
            var room = await _context.MeetingRooms.FindAsync(id);

            if (room == null)
                return NotFound(ApiResponseHelper.Fail<string>("Meeting room not found"));

            _context.MeetingRooms.Remove(room);
            await _context.SaveChangesAsync();

            return Ok(ApiResponseHelper.Success<string>(null, "Meeting room deleted successfully"));
        }
    }
}