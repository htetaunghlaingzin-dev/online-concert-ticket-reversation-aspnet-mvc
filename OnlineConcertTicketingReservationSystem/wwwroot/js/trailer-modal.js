document.querySelectorAll(".trailer-modal").forEach((modal) => {
  const media = modal.querySelector("iframe, video");
  if (!media) return;

  modal.addEventListener("shown.bs.modal", () => {
    const src = media.getAttribute("data-src");
    if (src) media.setAttribute("src", src);
    if (media.tagName === "VIDEO") media.play().catch(() => {});
  });

  modal.addEventListener("hidden.bs.modal", () => {
    if (media.tagName === "VIDEO") media.pause();
    media.setAttribute("src", "");
  });
});
