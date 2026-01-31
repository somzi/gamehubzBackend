namespace GameHubz.DataModels.Models
{
    public class MatchResultDetailDto
    {
        public string HomeUser { get; set; } = string.Empty;
        public string AwayUser { get; set; } = string.Empty;
        public int HomeUserScore { get; set; }
        public int AwayUserScore { get; set; }
        public List<string> Evidences { get; set; } = [];
    }
}