using AutoMapper;
using BookingSystem.Models;
using BookingSystem.Services.Dtos;
using BookingSystem.ViewModels;

namespace BookingSystem.Mappings;

/// <summary>
/// AutoMapper профіль для маппінгу Booking-пов'язаних типів.
///
/// Визначає дві межі:
///   1. ViewModel → DTO   (Controller → Service)
///   2. Entity   → DTO    (Service → Controller після збереження)
/// </summary>
public class BookingMappingProfile : Profile
{
    public BookingMappingProfile()
    {
        // ── ViewModel → DTO ──────────────────────────────────────────────────
        // Використовується в контролері перед передачею в сервіс.
        // StartTime/EndTime мають бути не null (вже провалідовано FluentValidation).
        CreateMap<BookingCreateViewModel, CreateBookingDto>()
            .ConstructUsing(src => new CreateBookingDto(
                src.ResourceId,
                src.StartTime!.Value,
                src.EndTime!.Value,
                src.Purpose));

        // ── Entity → DTO ─────────────────────────────────────────────────────
        // Використовується після збереження для повернення даних контролеру.
        CreateMap<Booking, BookingDto>()
            .ForMember(d => d.ResourceName,
                       o => o.MapFrom(s => s.Resource != null ? s.Resource.Name : string.Empty))
            .ForMember(d => d.StartTime,
                       o => o.MapFrom(s => s.StartTime!.Value))
            .ForMember(d => d.EndTime,
                       o => o.MapFrom(s => s.EndTime!.Value));
    }
}
