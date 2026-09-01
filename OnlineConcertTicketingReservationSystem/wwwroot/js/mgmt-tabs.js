(() => {
  const STORAGE_KEY = "mgmt-active-tab";
  const tabButtons = document.querySelectorAll("#mgmt-tabs [data-bs-toggle='tab']");
  if (tabButtons.length === 0) return;

  const savedTabId = localStorage.getItem(STORAGE_KEY);
  if (savedTabId) {
    const savedButton = document.getElementById(savedTabId);
    if (savedButton) {
      bootstrap.Tab.getOrCreateInstance(savedButton).show();
    }
  }

  tabButtons.forEach((button) => {
    button.addEventListener("shown.bs.tab", () => {
      localStorage.setItem(STORAGE_KEY, button.id);
    });
  });
})();
