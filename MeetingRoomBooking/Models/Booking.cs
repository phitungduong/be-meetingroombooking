namespace MeetingRoomBooking.Models
{
    public class Booking
    {
        public int Id { get; set; }

        public int MeetingRoomId { get; set; }

        public MeetingRoom MeetingRoom { get; set; }

        public string UserId { get; set; }

        public ApplicationUser User { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public string Status { get; set; } // Booked, Cancelled

        public DateTime CreatedAt { get; set; }
    }
}
