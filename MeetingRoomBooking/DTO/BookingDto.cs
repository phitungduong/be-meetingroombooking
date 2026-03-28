namespace MeetingRoomBooking.DTO
{
    public class BookingDto
    {
        public int Id { get; set; }
        public string RoomName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Status { get; set; }
    }
}
