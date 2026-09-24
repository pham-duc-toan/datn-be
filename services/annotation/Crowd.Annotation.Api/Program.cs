using System;
using System.Threading;
using Crowd.Annotation.Infrastructure.Persistence;
using Crowd.BuildingBlocks.Messaging;
using Crowd.BuildingBlocks.Persistence.Idempotency;
using Crowd.BuildingBlocks.Persistence.Outbox;
using Crowd.Contracts.Annotation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Crowd.Annotation.Api
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            string chuoiKetNoi = builder.Configuration.GetConnectionString("AnnotationDb")
                ?? throw new InvalidOperationException(
                    "Thieu ConnectionStrings:AnnotationDb trong appsettings.");

            builder.Services.AddDbContext<AnnotationDbContext>(options =>
            {
                options.UseNpgsql(chuoiKetNoi);
            });

            // Mot dong bat toan bo outbox: cho GHI (IOutboxWriter), cho GUI
            // (IOutboxPublisher), va worker nen (OutboxDispatcher).
            builder.Services.AddOutbox<AnnotationDbContext>(builder.Configuration);

            WebApplication app = builder.Build();

            app.MapGet("/", () => "annotation-svc");

            // -----------------------------------------------------------------
            // Endpoint TAM de chay thu duong di cua event. Se xoa khi co nghiep
            // vu that o phase P0.
            //
            // No lam dung ba viec cua "nua GHI":
            //   1. tao envelope
            //   2. xep vao hang cho gui
            //   3. SaveChanges — MOT transaction
            //
            // Sau do dispatcher tu nhat len va day sang RabbitMQ.
            // -----------------------------------------------------------------
            app.MapPost("/thu-nghiem/phat-event", async (
                AnnotationDbContext db,
                IOutboxWriter outbox) =>
            {
                AnnotationApproved ruot = new AnnotationApproved
                {
                    AnnotationId = Guid.CreateVersion7(),
                    TaskId = Guid.CreateVersion7(),
                    ProjectId = Guid.CreateVersion7(),
                    LabelerId = Guid.CreateVersion7(),
                    AmountVnd = 200000,
                    PlatformFeeVnd = 60000,
                    Source = AnnotationSource.Professional,
                };

                EventEnvelope<AnnotationApproved> thu = EventEnvelope.Create(
                    producer: "annotation-svc",
                    correlationId: Guid.CreateVersion7(),
                    payload: ruot);

                outbox.Enqueue(thu);

                // Trong nghiep vu that, dong nay cung commit luon thay doi tren
                // bang annotations. Day chinh la cho hai thu tro thanh nguyen tu.
                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    daXepVaoOutbox = thu.EventId,
                    eventType = thu.EventType,
                    ghiChu = "Doi khoang 500ms roi xem lai bang outbox va RabbitMQ.",
                });
            });

            // -----------------------------------------------------------------
            // Endpoint TAM: gia lap NHAN mot event tu bus.
            //
            // Goi hai lan voi cung eventId de thay idempotency lam viec.
            // "Nghiep vu" o day la xep mot event moi vao outbox — giong het
            // tinh huong that: consume mot event roi phat ra event ke tiep.
            // Neu lan hai bi chan dung cach thi outbox chi tang THEM MOT dong.
            // -----------------------------------------------------------------
            app.MapPost("/thu-nghiem/nhan-event", async (
                Guid eventId,
                AnnotationDbContext db,
                IIdempotencyGuard guard,
                IOutboxWriter outbox) =>
            {
                bool vuaXuLy = await guard.XuLyMotLanAsync(
                    eventId,
                    "ThuNghiemChiTra",
                    async ct =>
                    {
                        AnnotationApproved ruot = new AnnotationApproved
                        {
                            AnnotationId = Guid.CreateVersion7(),
                            TaskId = Guid.CreateVersion7(),
                            ProjectId = Guid.CreateVersion7(),
                            LabelerId = Guid.CreateVersion7(),
                            AmountVnd = 200000,
                            PlatformFeeVnd = 60000,
                            Source = AnnotationSource.Professional,
                        };

                        outbox.Enqueue(EventEnvelope.Create(
                            producer: "annotation-svc",
                            correlationId: Guid.CreateVersion7(),
                            payload: ruot));

                        await Task.CompletedTask;
                    },
                    CancellationToken.None);

                return Results.Ok(new
                {
                    eventId,
                    vuaXuLy,
                    ghiChu = vuaXuLy
                        ? "Lan dau — da chay nghiep vu"
                        : "Da xu ly tu truoc — BO QUA, nghiep vu khong chay lai",
                    soDongOutbox = await db.Outbox.CountAsync(),
                });
            });

            // Xem nhanh trang thai bang outbox ma khong phai mo psql.
            app.MapGet("/thu-nghiem/outbox", async (AnnotationDbContext db) =>
            {
                var dong = await db.Outbox
                    .OrderByDescending(x => x.Id)
                    .Take(10)
                    .Select(x => new
                    {
                        x.Id,
                        x.EventType,
                        x.PublishedAt,
                        x.AttemptCount,
                        x.LastError,
                        x.NextAttemptAt,
                    })
                    .ToListAsync();

                return Results.Ok(dong);
            });

            app.Run();
        }
    }
}
