using System;
using System.Collections.Generic;

namespace Crowd.Ledger.Api.Dtos
{
    public sealed class IssueInvoiceRequest
    {
        /// <summary>Thang da ket thuc, dang yyyy-MM.</summary>
        public string? Period { get; init; }
    }

    public sealed class InvoiceItemDto
    {
        public Guid? ProjectId { get; init; }

        public required string Description { get; init; }

        public required long Quantity { get; init; }

        public required long NetVnd { get; init; }

        public required long VatVnd { get; init; }

        public required long TotalVnd { get; init; }
    }

    public sealed class PassThroughItemDto
    {
        public Guid? ProjectId { get; init; }

        public required string Description { get; init; }

        public required long AmountVnd { get; init; }
    }

    /// <summary>Tam tinh mot thang (chua xuat, chua co so hoa don).</summary>
    public sealed class InvoicePreviewResponse
    {
        public required string Period { get; init; }

        public required int VatPercent { get; init; }

        public required IReadOnlyList<InvoiceItemDto> Items { get; init; }

        public required long TotalNetVnd { get; init; }

        public required long TotalVatVnd { get; init; }

        public required long TotalVnd { get; init; }

        public required long PassThroughVnd { get; init; }

        /// <summary>Thang nay da co hoa don chua.</summary>
        public required bool Issued { get; init; }
    }

    public sealed class InvoiceResponse
    {
        public required Guid InvoiceId { get; init; }

        public required Guid BusinessId { get; init; }

        public required string Period { get; init; }

        public required string Serial { get; init; }

        /// <summary>So hoa don 7 chu so ("0000123").</summary>
        public required string Number { get; init; }

        public required DateTimeOffset IssuedAt { get; init; }

        public required string BuyerName { get; init; }

        public required string BuyerTaxCode { get; init; }

        public required string BuyerAddress { get; init; }

        public required string SellerName { get; init; }

        public required string SellerTaxCode { get; init; }

        public required string SellerAddress { get; init; }

        public required int VatPercent { get; init; }

        public required long TotalNetVnd { get; init; }

        public required long TotalVatVnd { get; init; }

        public required long TotalVnd { get; init; }

        public required string AmountInWords { get; init; }

        public required IReadOnlyList<InvoiceItemDto> Items { get; init; }

        /// <summary>Bang ke tien chi ho labeler / sharer (khong chiu VAT, khong nam trong tong hoa don).</summary>
        public required IReadOnlyList<PassThroughItemDto> PassThrough { get; init; }

        public required long PassThroughVnd { get; init; }
    }
}
