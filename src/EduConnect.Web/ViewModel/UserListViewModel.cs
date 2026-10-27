using EduConnect.Web.Models;

namespace EduConnect.Web.ViewModels
{
    // /Admin/Users: filters in, one page of users out. Shared by the full page
    // and the _UsersTable partial the live search swaps in.
    public class UserListViewModel
    {
        public const int PageSize = 15;

        public string? SearchQuery { get; set; }
        public string? FilterRole { get; set; }
        public string? FilterStatus { get; set; }
        public string? FilterPlacement { get; set; }
        public int? FilterCollege { get; set; }
        public int Page { get; set; } = 1;

        public int TotalCount { get; set; }
        public List<User> Users { get; set; } = new();

        public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);

        // A page number outside 1..PageCount shows the nearest real page.
        public int ClampedPage(int page) => Math.Clamp(page, 1, PageCount);

        public int FirstShown => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
        public int LastShown => Math.Min(Page * PageSize, TotalCount);

        // The first, the last, and two either side of the current page; a null
        // marks a gap the pager draws as "…".
        public List<int?> PageNumbers()
        {
            var shown = Enumerable.Range(1, PageCount)
                .Where(p => p == 1 || p == PageCount || Math.Abs(p - Page) <= 2);
            var result = new List<int?>();
            foreach (var p in shown)
            {
                if (result.Count > 0 && p - result[^1] > 1)
                    result.Add(null);
                result.Add(p);
            }
            return result;
        }

        // Route values for a page link: every filter that is set, plus the page.
        public Dictionary<string, string> RouteFor(int page)
        {
            var values = new Dictionary<string, string>();
            void Add(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value)) values[key] = value;
            }
            Add("searchQuery", SearchQuery);
            Add("filterRole", FilterRole);
            Add("filterStatus", FilterStatus);
            Add("filterCollege", FilterCollege?.ToString());
            Add("filterPlacement", FilterPlacement);
            values["page"] = page.ToString();
            return values;
        }
    }
}
