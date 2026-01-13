using Template.DataModels.Interfaces;

namespace Template.DataModels.Models
{
    public class Asset : IEditableDto
    {
        public Guid? Id { get; set; }

        public DateTime? CreatedOn { get; set; }

        public DateTime? ModifiedOn { get; set; }

        public string FileName { get; set; } = "";

        public string FileFormat { get; set; } = "";

        public string Extension { get; set; } = "";

        public int? AssetTypeId { get; set; }

        public string Description { get; set; } = "";

        public string Url { get; set; } = "";

        public string ThumbUrl { get; set; } = "";

        public string Size { get; set; } = "";

        public Guid? CreatedBy { get; set; }

        public string CreatedByName { get; set; } = "";

        public Guid? ModifiedBy { get; set; }

        public string ModifiedByName { get; set; } = "";
    }
}