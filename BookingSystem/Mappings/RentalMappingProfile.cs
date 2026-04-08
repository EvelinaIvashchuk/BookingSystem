using AutoMapper;
using BookingSystem.Models;
using BookingSystem.Services.Dtos;
using BookingSystem.ViewModels;

namespace BookingSystem.Mappings;

public class RentalMappingProfile : Profile
{
    public RentalMappingProfile()
    {
        CreateMap<RentalCreateViewModel, CreateRentalDto>()
            .ConstructUsing(src => new CreateRentalDto(
                src.CarId,
                src.PickupDate!.Value,
                src.ReturnDate!.Value,
                src.Notes));

        CreateMap<Rental, RentalDto>()
            .ForMember(d => d.CarName,
                       o => o.MapFrom(s => s.Car != null ? s.Car.FullName : string.Empty))
            .ForMember(d => d.PickupDate,
                       o => o.MapFrom(s => s.PickupDate!.Value))
            .ForMember(d => d.ReturnDate,
                       o => o.MapFrom(s => s.ReturnDate!.Value));
    }
}
