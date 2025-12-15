using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using TravelAgency.Models;

namespace TravelAgency.Controllers
{
    public class PackageController : Controller
    {
        private readonly string _connectionString;

        public PackageController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        // =========================
        // 1) Start search page
        // =========================
        [HttpGet]
        public IActionResult SearchStart(
            string? searchText = "",
            string? destination = "",
            string? country = "",
            int? categoryId = null,
            int adults = 1,
            int children = 0,
            int? youngestAge = null,
            int? month = null,
            int? year = null
        )
        {
            ViewBag.SearchText = searchText ?? "";
            ViewBag.Destination = destination ?? "";
            ViewBag.Country = country ?? "";
            ViewBag.CategoryId = categoryId;

            ViewBag.Adults = adults < 1 ? 1 : adults;
            ViewBag.Children = children < 0 ? 0 : children;
            ViewBag.YoungestAge = youngestAge;

            ViewBag.Month = month;
            ViewBag.Year = year;

            return View();
        }

        // =========================
        // 2) Suggestions endpoint (AJAX)
        // =========================
        [HttpGet]
        public IActionResult Suggest(string q)
        {
            q = (q ?? "").Trim();

            if (q.Length < 2)
                return Json(new List<SearchSuggestion>());

            var list = new List<SearchSuggestion>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT TOP (20)
                        p.destination,
                        ISNULL(p.country, '') as country,
                        p.idCategory,
                        ISNULL(c.name, '') as categoryName
                    FROM Package p
                    INNER JOIN Category c ON c.Id = p.idCategory AND c.inactive = 0
                    WHERE p.inactive = 0
                      AND (
                           p.destination LIKE @q + '%'
                           OR p.country LIKE @q + '%'
                           OR c.name LIKE @q + '%'
                      )
                    ORDER BY p.destination, p.country, c.name;
                ";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@q", q);

                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            string dest = r.GetString(0);
                            string ctry = r.GetString(1);
                            int catId = r.GetInt32(2);
                            string catName = r.GetString(3);

                            // A) Destination – Country
                            list.Add(new SearchSuggestion
                            {
                                Destination = dest,
                                Country = ctry,
                                CategoryId = null,
                                CategoryName = null,
                                DisplayText = $"{dest} – {ctry}"
                            });

                            // B) Destination – Country · Category Package
                            list.Add(new SearchSuggestion
                            {
                                Destination = dest,
                                Country = ctry,
                                CategoryId = catId,
                                CategoryName = catName,
                                DisplayText = $"{dest} – {ctry} · {catName} Package"
                            });
                        }
                    }
                }
            }

            var finalList = list
                .GroupBy(x => x.DisplayText)
                .Select(g => g.First())
                .Take(10)
                .ToList();

            return Json(finalList);
        }

        // =========================
        // 3) Results page (Gallery) + FILTER + SORT + DISCOUNT + IMAGES + WAITING COUNT
        //    ✅ + AVG RATING + TOTAL REVIEWS (NEW)
        // =========================
        [HttpGet]
        public IActionResult Gallery(
            string? searchText = "",
            string? destination = "",
            string? country = "",
            int? categoryId = null,
            int adults = 1,
            int children = 0,
            int? youngestAge = null,
            int? month = null,
            int? year = null,

            string? priceRange = "",          // "", "low", "mid", "high"
            int? minPrice = null,             // optional future
            int? maxPrice = null,             // optional future
            string? sortBy = "travelDate",    // travelDate, priceAsc, priceDesc, popular, category
            bool onSaleOnly = false
        )
        {
            adults = adults < 1 ? 1 : adults;
            children = children < 0 ? 0 : children;

            int totalPassengers = adults + children;

            // Server validation: youngestAge required when children>0
            if (children > 0 && (!youngestAge.HasValue || youngestAge < 0 || youngestAge > 17))
            {
                TempData["SearchError"] = "Please enter the youngest child age (0–17).";
                return RedirectToAction("SearchStart", new
                {
                    searchText,
                    destination,
                    country,
                    categoryId,
                    adults,
                    children,
                    youngestAge,
                    month,
                    year
                });
            }

            string freeText = (searchText ?? "").Trim();

            // אם נבחרה הצעה (destination/country/categoryId) – לא עושים freeText LIKE
            bool hasChosenSuggestion =
                !string.IsNullOrWhiteSpace(destination) ||
                !string.IsNullOrWhiteSpace(country) ||
                categoryId.HasValue;

            var packages = new List<Package>();
            var imagesByPackage = new Dictionary<int, List<string>>();

            // waiting count per package
            var waitingCountByPackage = new Dictionary<int, int>();

            // ✅ NEW: rating maps per package (avg + count)
            var avgRatingByPackage = new Dictionary<int, double>();
            var totalReviewsByPackage = new Dictionary<int, int>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT
                        p.Id,
                        p.destination,
                        p.country,
                        p.startDate,
                        p.endDate,
                        p.sum,
                        p.ageLimit,
                        p.numFreePlaces,
                        p.idCategory,
                        p.Information,
                        p.cancelationDays,
                        p.inactive,
                        c.name as CategoryName,

                        ISNULL(h.TotalBookings, 0) as TotalBookings,
                        d.DiscountPercent as DiscountPercent,

                        ISNULL(w.WaitingCount, 0) as WaitingCount,

                        ISNULL(fb.AvgRate, 0) as AvgRate,
                        ISNULL(fb.TotalReviews, 0) as TotalReviews

                    FROM Package p
                    INNER JOIN Category c ON c.Id = p.idCategory AND c.inactive = 0

                    LEFT JOIN (
                        SELECT PackageId, COUNT(*) as TotalBookings
                        FROM HistoryReservation
                        WHERE inactive = 0
                        GROUP BY PackageId
                    ) h ON h.PackageId = p.Id

                    OUTER APPLY (
                        SELECT TOP (1) dd.DiscountPercent
                        FROM Discount dd
                        WHERE dd.PackageId = p.Id
                          AND dd.IsActive = 1
                          AND dd.StartDate <= GETDATE()
                          AND (dd.EndDate IS NULL OR dd.EndDate >= GETDATE())
                        ORDER BY dd.DiscountPercent DESC, dd.StartDate DESC, dd.Id DESC
                    ) d

                    OUTER APPLY (
                        SELECT COUNT(*) as WaitingCount
                        FROM WaitingList wl
                        WHERE wl.PackageId = p.Id
                          AND wl.inactive = 0
                    ) w

                    OUTER APPLY (
                        SELECT
                            AVG(CAST(f.Rate as float)) as AvgRate,
                            COUNT(*) as TotalReviews
                        FROM PackageFeedback pf
                        INNER JOIN feedBack1 f ON f.Id = pf.FeedbackId
                        WHERE pf.PackageId = p.Id
                          AND pf.inactive = 0
                          AND f.inactive = 0
                          AND f.feedbackType = 'Package'
                    ) fb

                    WHERE p.inactive = 0
                ";

                var conditions = new List<string>();
                var cmd = new SqlCommand { Connection = conn };

                if (!string.IsNullOrWhiteSpace(destination))
                {
                    conditions.Add("p.destination = @dest");
                    cmd.Parameters.AddWithValue("@dest", destination.Trim());
                }

                if (!string.IsNullOrWhiteSpace(country))
                {
                    conditions.Add("p.country = @ctry");
                    cmd.Parameters.AddWithValue("@ctry", country.Trim());
                }

                if (categoryId.HasValue)
                {
                    conditions.Add("p.idCategory = @catId");
                    cmd.Parameters.AddWithValue("@catId", categoryId.Value);
                }

                if (month.HasValue && month.Value >= 1 && month.Value <= 12)
                {
                    conditions.Add("MONTH(p.startDate) = @m");
                    cmd.Parameters.AddWithValue("@m", month.Value);
                }

                if (year.HasValue && year.Value >= 2000 && year.Value <= 2100)
                {
                    conditions.Add("YEAR(p.startDate) = @y");
                    cmd.Parameters.AddWithValue("@y", year.Value);
                }

                if (!hasChosenSuggestion && !string.IsNullOrWhiteSpace(freeText))
                {
                    conditions.Add("(p.destination LIKE '%' + @ft + '%' OR p.country LIKE '%' + @ft + '%' OR c.name LIKE '%' + @ft + '%')");
                    cmd.Parameters.AddWithValue("@ft", freeText);
                }

               
                if (children > 0 && youngestAge.HasValue)
                {
                    conditions.Add("p.ageLimit <= @youngestAge");
                    cmd.Parameters.AddWithValue("@youngestAge", youngestAge.Value);
                }

                
                if (onSaleOnly)
                {
                    conditions.Add("(d.DiscountPercent IS NOT NULL AND d.DiscountPercent > 0)");
                }

                string pr = (priceRange ?? "").Trim().ToLowerInvariant();
                if (pr == "low")
                {
                    conditions.Add("p.sum < 500");
                }
                else if (pr == "mid")
                {
                    conditions.Add("p.sum >= 500 AND p.sum <= 3000");
                }
                else if (pr == "high")
                {
                    conditions.Add("p.sum > 3000");
                }
                else if (pr == "custom")
                {
                    if (minPrice.HasValue)
                    {
                        conditions.Add("p.sum >= @minP");
                        cmd.Parameters.AddWithValue("@minP", minPrice.Value);
                    }
                    if (maxPrice.HasValue)
                    {
                        conditions.Add("p.sum <= @maxP");
                        cmd.Parameters.AddWithValue("@maxP", maxPrice.Value);
                    }
                }

                if (conditions.Count > 0)
                {
                    sql += " AND " + string.Join(" AND ", conditions);
                }

                string sb = (sortBy ?? "travelDate").Trim().ToLowerInvariant();
                string effectivePriceExpr = "CAST(p.sum * (1 - (ISNULL(d.DiscountPercent,0) / 100.0)) AS decimal(18,2))";

                string orderBy =
                    sb == "priceasc" ? $"{effectivePriceExpr} ASC, p.startDate ASC" :
                    sb == "pricedesc" ? $"{effectivePriceExpr} DESC, p.startDate ASC" :
                    sb == "popular" ? "ISNULL(h.TotalBookings,0) DESC, p.startDate ASC" :
                    sb == "category" ? "c.name ASC, p.startDate ASC" :
                    "p.startDate ASC, p.destination ASC";

                sql += $" ORDER BY {orderBy};";

                cmd.CommandText = sql;

                using (var r = cmd.ExecuteReader())
                {
                    int oId = r.GetOrdinal("Id");
                    int oDest = r.GetOrdinal("destination");
                    int oCountry = r.GetOrdinal("country");
                    int oStart = r.GetOrdinal("startDate");
                    int oEnd = r.GetOrdinal("endDate");
                    int oSum = r.GetOrdinal("sum");
                    int oAge = r.GetOrdinal("ageLimit");
                    int oFree = r.GetOrdinal("numFreePlaces");
                    int oCatId = r.GetOrdinal("idCategory");
                    int oInfo = r.GetOrdinal("Information");
                    int oCancel = r.GetOrdinal("cancelationDays");
                    int oInactive = r.GetOrdinal("inactive");
                    int oCatName = r.GetOrdinal("CategoryName");
                    int oTotalBookings = r.GetOrdinal("TotalBookings");
                    int oDiscount = r.GetOrdinal("DiscountPercent");
                    int oWaiting = r.GetOrdinal("WaitingCount");
                    int oAvgRate = r.GetOrdinal("AvgRate");
                    int oTotalReviews = r.GetOrdinal("TotalReviews");

                    while (r.Read())
                    {
                        var pkg = new Package
                        {
                            Id = r.GetInt32(oId),
                            destination = r.GetString(oDest),
                            country = r.IsDBNull(oCountry) ? null : r.GetString(oCountry),
                            StartDate = r.GetDateTime(oStart),
                            EndDate = r.GetDateTime(oEnd),
                            sum = r.GetInt32(oSum),
                            ageLimit = r.GetInt32(oAge),
                            numFreePlaces = r.GetInt32(oFree),
                            idCategory = r.GetInt32(oCatId),
                            information = r.IsDBNull(oInfo) ? "" : r.GetString(oInfo),
                            cancelationDays = r.IsDBNull(oCancel) ? (int?)null : r.GetInt32(oCancel),
                            inactive = r.GetBoolean(oInactive),
                            CategoryName = r.IsDBNull(oCatName) ? null : r.GetString(oCatName),
                            TotalBookings = r.IsDBNull(oTotalBookings) ? 0 : r.GetInt32(oTotalBookings),
                            DiscountPercent = r.IsDBNull(oDiscount) ? (int?)null : r.GetInt32(oDiscount)
                        };

                        int waitingCount = r.IsDBNull(oWaiting) ? 0 : r.GetInt32(oWaiting);
                        waitingCountByPackage[pkg.Id] = waitingCount;

                        double avgRate = r.IsDBNull(oAvgRate) ? 0.0 : r.GetDouble(oAvgRate);
                        int totalRev = r.IsDBNull(oTotalReviews) ? 0 : r.GetInt32(oTotalReviews);

                        avgRatingByPackage[pkg.Id] = avgRate;
                        totalReviewsByPackage[pkg.Id] = totalRev;

                        packages.Add(pkg);
                    }
                }

                if (packages.Count > 0)
                {
                    var ids = packages.Select(p => p.Id).Distinct().ToList();

                    var inParams = new List<string>();
                    var imgCmd = new SqlCommand { Connection = conn };

                    for (int i = 0; i < ids.Count; i++)
                    {
                        string param = "@id" + i;
                        inParams.Add(param);
                        imgCmd.Parameters.AddWithValue(param, ids[i]);
                    }

                    imgCmd.CommandText = $@"
                        SELECT PackageId, ImageLocation
                        FROM ImagesPackage
                        WHERE PackageId IN ({string.Join(",", inParams)})
                        ORDER BY PackageId, Id;
                    ";

                    using (var ir = imgCmd.ExecuteReader())
                    {
                        while (ir.Read())
                        {
                            int pid = ir.GetInt32(0);
                            string loc = ir.GetString(1);

                            if (!imagesByPackage.ContainsKey(pid))
                                imagesByPackage[pid] = new List<string>();

                            imagesByPackage[pid].Add(loc);
                        }
                    }

                    foreach (var p in packages)
                    {
                        if (imagesByPackage.TryGetValue(p.Id, out var imgs) && imgs.Count > 0)
                            p.ImageUrl = imgs[0];
                        else
                            p.ImageUrl = "/images/default.jpg";
                    }
                }
            }

            ViewBag.SearchText = searchText ?? "";
            ViewBag.Destination = destination ?? "";
            ViewBag.Country = country ?? "";
            ViewBag.CategoryId = categoryId;

            ViewBag.Adults = adults;
            ViewBag.Children = children;
            ViewBag.YoungestAge = youngestAge;

            ViewBag.Month = month;
            ViewBag.Year = year;

            ViewBag.PriceRange = priceRange ?? "";
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;
            ViewBag.SortBy = sortBy ?? "travelDate";
            ViewBag.OnSaleOnly = onSaleOnly;

            ViewBag.ImagesByPackage = imagesByPackage;
            ViewBag.WaitingCountByPackage = waitingCountByPackage;

            ViewBag.AvgRatingByPackage = avgRatingByPackage;
            ViewBag.TotalReviewsByPackage = totalReviewsByPackage;

            return View(packages);
        }

        // =========================
        // 4) Package Details (VIEW) - specific package by Id
        // =========================
        [HttpGet]
        public IActionResult PackageDetails(int id, int adults = 1, int children = 0)
        {
            adults = adults < 1 ? 1 : adults;
            children = children < 0 ? 0 : children;
            int totalPassengers = adults + children;

            Package? pkg = null;
            string categoryName = "";
            int discountedPrice = 0;

            var images = new List<string>();
            var reviews = new List<Feedback>();

            double avgRating = 0;
            int totalReviews = 0;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                string sql = @"
                    SELECT
                        p.Id,
                        p.destination,
                        p.country,
                        p.startDate,
                        p.endDate,
                        p.sum,
                        p.ageLimit,
                        p.numFreePlaces,
                        p.idCategory,
                        p.Information,
                        p.cancelationDays,
                        p.inactive,
                        c.name as CategoryName,
                        d.DiscountPercent as DiscountPercent
                    FROM Package p
                    INNER JOIN Category c ON c.Id = p.idCategory AND c.inactive = 0

                    OUTER APPLY (
                        SELECT TOP (1) dd.DiscountPercent
                        FROM Discount dd
                        WHERE dd.PackageId = p.Id
                          AND dd.IsActive = 1
                          AND dd.StartDate <= GETDATE()
                          AND (dd.EndDate IS NULL OR dd.EndDate >= GETDATE())
                        ORDER BY dd.DiscountPercent DESC, dd.StartDate DESC, dd.Id DESC
                    ) d

                    WHERE p.Id = @Id AND p.inactive = 0;
                ";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@Id", id);

                    using (var r = cmd.ExecuteReader())
                    {
                        if (!r.Read())
                        {
                            TempData["SearchError"] = "Trip not found.";
                            return RedirectToAction("Gallery");
                        }

                        pkg = new Package
                        {
                            Id = r.GetInt32(0),
                            destination = r.GetString(1),
                            country = r.IsDBNull(2) ? null : r.GetString(2),
                            StartDate = r.GetDateTime(3),
                            EndDate = r.GetDateTime(4),
                            sum = r.GetInt32(5),
                            ageLimit = r.GetInt32(6),
                            numFreePlaces = r.GetInt32(7),
                            idCategory = r.GetInt32(8),
                            information = r.IsDBNull(9) ? "" : r.GetString(9),
                            cancelationDays = r.IsDBNull(10) ? (int?)null : r.GetInt32(10),
                            inactive = r.GetBoolean(11),
                            CategoryName = r.IsDBNull(12) ? "" : r.GetString(12),
                            DiscountPercent = r.IsDBNull(13) ? (int?)null : r.GetInt32(13)
                        };

                        categoryName = pkg.CategoryName ?? "";
                    }
                }

                string imgSql = @"
                    SELECT ImageLocation
                    FROM ImagesPackage
                    WHERE PackageId = @Id
                    ORDER BY Id;
                ";
                using (var imgCmd = new SqlCommand(imgSql, conn))
                {
                    imgCmd.Parameters.AddWithValue("@Id", id);
                    using (var ir = imgCmd.ExecuteReader())
                    {
                        while (ir.Read())
                            images.Add(ir.GetString(0));
                    }
                }

                string reviewSql = @"
                    SELECT
                        f.Id,
                        f.userId,
                        ISNULL(u.firstName + ' ' + u.lastName, u.Username) as UserFullName,
                        ISNULL(f.Description,'') as Description,
                        f.Rate,
                        f.feedbackType,
                        f.inactive
                    FROM PackageFeedback pf
                    INNER JOIN feedBack1 f ON f.Id = pf.FeedbackId
                    LEFT JOIN Users u ON u.Id = f.userId
                    WHERE pf.PackageId = @Id
                      AND pf.inactive = 0
                      AND f.inactive = 0
                      AND f.feedbackType = 'Package'
                    ORDER BY f.Id DESC;
                ";
                using (var revCmd = new SqlCommand(reviewSql, conn))
                {
                    revCmd.Parameters.AddWithValue("@Id", id);
                    using (var rr = revCmd.ExecuteReader())
                    {
                        while (rr.Read())
                        {
                            reviews.Add(new Feedback
                            {
                                Id = rr.GetInt32(0),
                                UserId = rr.IsDBNull(1) ? 0 : rr.GetInt32(1),
                                UserFullName = rr.IsDBNull(2) ? "Anonymous" : rr.GetString(2),
                                Description = rr.IsDBNull(3) ? "" : rr.GetString(3),
                                Rate = rr.GetInt32(4),
                                feedbackType = rr.IsDBNull(5) ? "" : rr.GetString(5),
                                inactive = rr.GetBoolean(6)
                            });
                        }
                    }
                }

                totalReviews = reviews.Count;
                avgRating = (totalReviews == 0) ? 0 : reviews.Average(x => x.Rate);
            }

            if (images.Count == 0) images.Add("/images/default.jpg");
            pkg!.ImageUrl = images[0];

            discountedPrice = pkg.sum;
            if (pkg.DiscountPercent.HasValue && pkg.DiscountPercent.Value > 0)
            {
                discountedPrice = (int)Math.Round(pkg.sum * (1 - (pkg.DiscountPercent.Value / 100.0)));
            }

            bool isFull = (pkg.numFreePlaces <= 0) || (pkg.numFreePlaces < totalPassengers);
            bool isAlmostFull = (!isFull) && (pkg.numFreePlaces > 0 && pkg.numFreePlaces <= 5);

            var vm = new PackageDetailsViewModel
            {
                Package = pkg!,
                CategoryName = categoryName,
                Reviews = reviews,

                TotalPriceMultiplier = totalPassengers
            };

            ViewBag.Images = images;
            return View(vm);


        }
    }
} 
