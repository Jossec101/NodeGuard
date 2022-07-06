﻿using FundsManager.Data.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FundsManager.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.EnableDetailedErrors();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        modelBuilder.Entity<ChannelOperationRequest>()
                .HasOne(cor => cor.SourceNode)
                .WithMany(node => node.ChannelOperationRequestsAsSource)
                .HasForeignKey(cor => cor.SourceNodeId);
            modelBuilder.Entity<ChannelOperationRequest>()
                .HasOne(cor => cor.DestNode)
                .WithMany(node => node.ChannelOperationRequestsAsDestination)
                .HasForeignKey(cor => cor.DestNodeId);
            base.OnModelCreating(modelBuilder);

        }

        public DbSet<ApplicationUser> ApplicationUsers { get; set; }

        public DbSet<Key> Keys { get; set; }

        public DbSet<Node> Nodes { get; set; }

        public DbSet<Wallet> Wallets { get; set; }

        public DbSet<ChannelOperationRequest> ChannelOperationRequests { get; set; }

        public DbSet<ChannelOperationRequestSignature> ChannelOperationRequestSignatures { get; set; }

        public DbSet<Channel> Channels { get; set; }

    }
}