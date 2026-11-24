namespace PropertyShowing.Infrastructure.Data;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<ShowingSlot> Slots => Set<ShowingSlot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingHistory> BookingHistories => Set<BookingHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShowingSlot>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.BookedSeats).IsConcurrencyToken();
            entity.ToTable(table => table.HasCheckConstraint("CK_Slots_Capacity", "Capacity BETWEEN 1 AND 20"));
            entity.ToTable(table => table.HasCheckConstraint("CK_Slots_BookedSeats", "BookedSeats >= 0 AND BookedSeats <= Capacity"));
            entity.HasMany(x => x.Bookings).WithOne(x => x.Slot).HasForeignKey(x => x.SlotId);
        });

        modelBuilder.Entity<Booking>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RenterEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Status).IsConcurrencyToken();
            entity.HasIndex(x => new { x.SlotId, x.RenterEmail })
                .IsUnique()
                .HasFilter("Status = 0");
            entity.HasMany(x => x.History).WithOne(x => x.Booking).HasForeignKey(x => x.BookingId);
        });

        modelBuilder.Entity<BookingHistory>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Actor).HasMaxLength(20).IsRequired();
        });
    }
}
