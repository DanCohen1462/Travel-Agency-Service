-- Database schema for Travel Agency Project

-- auto-generated definition
create table Category
(
    Id       int identity
        primary key,
    name     nvarchar(50)  not null,
    inactive bit default 0 not null
)
    go

-- auto-generated definition
create table Discount
(
    Id              int identity
        primary key,
    PackageId       int           not null,
    DiscountPercent int           not null,
    IsActive        bit default 1 not null,
    StartDate       datetime      not null,
    EndDate         datetime
)
    go

-- auto-generated definition
create table feedBack1
(
    Id           int identity
        primary key,
    userId       int,
    Description  nvarchar(max),
    Rate         int           not null,
    feedbackType nvarchar(50)  not null,
    inactive     bit default 0 not null
)
    go

-- auto-generated definition
create table HistoryReservation
(
    Id         int identity
        primary key,
    UserId     int           not null,
    PackageId  int           not null,
    inactive   bit default 0 not null,
    numPersons int default 1,
    sum        int default 0
)
    go


-- auto-generated definition
create table ImagesPackage
(
    Id            int identity
        primary key,
    PackageId     int          not null
        references Package,
    ImageLocation varchar(max) not null
)
    go

-- auto-generated definition
create table Notifications
(
    Id        int identity
        primary key,
    UserId    int                                                  not null
        constraint FK_Notifications_Users
            references Users,
    Title     nvarchar(120)                                        not null,
    Message   nvarchar(500)                                        not null,
    Type      nvarchar(20)                                         not null,
    LinkUrl   nvarchar(300),
    IsRead    bit
        constraint DF_Notifications_IsRead default 0               not null,
    CreatedAt datetime
        constraint DF_Notifications_CreatedAt default getutcdate() not null,
    inactive  bit
        constraint DF_Notifications_inactive default 0             not null
)
    go


-- auto-generated definition
create table Package
(
    Id              int identity
        primary key,
    destination     nvarchar(50)  not null,
    startDate       date          not null,
    endDate         date          not null,
    sum             int           not null,
    ageLimit        int           not null,
    numFreePlaces   int           not null,
    idCategory      int           not null,
    UserId          int           not null,
    Information     nvarchar(max),
    inactive        bit default 0 not null,
    country         nvarchar(100),
    cancelationDays int
)
    go
    
    
-- auto-generated definition
create table PackageFeedback
(
    Id          int identity
        primary key,
    PackageId   int                       not null,
    FeedbackId  int                       not null,
    inactive    bit           default 0   not null,
    CategoryId  int           default 0   not null,
    Destination nvarchar(200) default ' ' not null,
    Country     nvarchar(200) default ' ' not null
)
    go

-- auto-generated definition
create table shoppingcart
(
    Id         int identity
        primary key,
    userId     int                                                                  not null,
    PackageId  int                                                                  not null,
    sum        int                                                                  not null,
    inactive   bit default 0                                                        not null,
    numPersons int default 1                                                        not null,
    CreatedAt  datetime
        constraint DF_shoppingcart_CreatedAt default getdate()                      not null,
    ExpiresAt  datetime
        constraint DF_shoppingcart_ExpiresAt default dateadd(minute, 15, getdate()) not null,
    OfferId    int
)
    go

-- auto-generated definition
create table types
(
    id   int          not null
        constraint taypes_pk
            primary key,
    name nvarchar(50) not null
)
    go

-- auto-generated definition
create table Users
(
    Id                   int identity
        primary key,
    Username             nvarchar(50)  not null
        unique,
    Password             nvarchar(200) not null,
    firstName            nvarchar(200) not null,
    lastName             nvarchar(200) not null,
    type                 int           not null,
    birthDate            date,
    gender               nvarchar(20),
    phoneNumber          nvarchar(20),
    email                nvarchar(200),
    inactive             bit default 0 not null,
    LastUsernameChangeAt datetime
)
    go

-- auto-generated definition
create table WaitingList
(
    Id               int identity
        primary key,
    UserId           int                            not null
        constraint FK_WaitingList_Users
            references Users,
    PackageId        int                            not null
        constraint FK_WaitingList_Package
            references Package,
    JoinDate         datetime     default getdate() not null,
    inactive         bit          default 0         not null,
    notificationDate datetime,
    numPersons       int          default 1,
    Reason           nvarchar(20) default 'full'    not null
        constraint CK_WaitingList_Reason
            check ([Reason] = 'cart' OR [Reason] = 'full')
)
    go

-- auto-generated definition
create table WaitlistOffers
(
    Id         int identity
        primary key,
    PackageId  int                                                not null
        constraint FK_WaitlistOffers_Package
            references Package,
    UserId     int                                                not null
        constraint FK_WaitlistOffers_Users
            references Users,
    NumPersons int                                                not null,
    Reason     nvarchar(20)                                       not null,
    OfferStart datetime
        constraint DF_WaitlistOffers_OfferStart default getdate() not null,
    OfferEnd   datetime                                           not null,
    IsUsed     bit
        constraint DF_WaitlistOffers_IsUsed default 0             not null,
    UsedAt     datetime,
    IsExpired  as case when [OfferEnd] < getdate() AND [IsUsed] = 0 then 1 else 0 end not null,
    ExpiredAt  datetime
)
    go

create index IX_WaitlistOffers_Active
    on WaitlistOffers (PackageId, UserId, IsUsed, OfferEnd)
    go

create index IX_WaitlistOffers_Package_Active
    on WaitlistOffers (PackageId, IsUsed, OfferEnd)
    go

create index IX_WaitlistOffers_User_Active
    on WaitlistOffers (UserId, IsUsed, OfferEnd)
    go
