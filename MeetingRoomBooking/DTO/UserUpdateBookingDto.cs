namespace MeetingRoomBooking.DTO
{
    public class UserUpdateBookingDto
    {
        public int MeetingRoomId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }
}
