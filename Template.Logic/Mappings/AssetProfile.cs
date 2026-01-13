using AutoMapper;
using Microsoft.Extensions.Configuration;

namespace Template.Logic.Mappings
{
    public class AssetProfile : Profile
    {
        private readonly string[] sizeSuffixes = { "B", "KB", "MB", "GB", "TB" };

        public AssetProfile()
        {
        }

        public AssetProfile(IConfiguration configuration)
        {
            this.CreateMap<AssetEntity, Asset>()
                .ForMember(x => x.AssetTypeId, m => m.MapFrom(x => (int)x.AssetType))
                .ForMember(x => x.Size, m => m.MapFrom(x => this.ToByteSizeString(x.Size, 1)))

                .ForMember(x => x.CreatedByName, m => m.MapFrom(x => $"{(x.CreatedByUser != null ? x.CreatedByUser.FirstName : "")} " +
                $"{(x.CreatedByUser != null ? x.CreatedByUser.LastName : "")}"))

                .ForMember(x => x.ModifiedByName, m => m.MapFrom(x => $"{(x.ModifiedByUser != null ? x.ModifiedByUser.FirstName : "")} " +
                $"{(x.ModifiedByUser != null ? x.ModifiedByUser.LastName : "")}"))

                .ForMember(x => x.Url, m => m.MapFrom(x => BlobUrlHelper.GetUrl(configuration, x)))
                .ForMember(x => x.ThumbUrl, m => m.MapFrom(x => BlobUrlHelper.GetThumbUrl(configuration, x)));

            this.CreateMap<AssetEntity, TokenUserInfo>();
            this.CreateMap<AssetUpdate, AssetEntity>();
        }

        private string ToByteSizeString(long value, int decimalPlaces)
        {
            if (value == 0)
            {
                return string.Format("{0:n" + decimalPlaces + "} B", 0);
            }

            int log = (int)Math.Log(value, 1024);

            decimal adjustedSize = (decimal)value / (1L << (log * 10));

            if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
            {
                log += 1;
                adjustedSize /= 1024;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}", adjustedSize, this.sizeSuffixes[log]);
        }
    }
}