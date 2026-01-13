using Microsoft.AspNetCore.Http;

namespace Template.DataModels.Models
{
    public class AssetUpload
    {
        public IFormFile? File { get; set; }

        public int AssetTypeId { get; set; }

        public string Description { get; set; } = "";
    }
}