namespace Template.DataModels.Models
{
    public class LookupResponse
    {
        public LookupResponse()
        {
        }

        public LookupResponse(Guid id, string displayText)
        {
            this.Id = id;
            this.DisplayText = displayText;
        }

        public Guid Id { get; set; }

        public string DisplayText { get; set; } = "";
    }
}