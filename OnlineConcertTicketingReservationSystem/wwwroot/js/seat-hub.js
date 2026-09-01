(function () {
  const gridEl = document.getElementById("seat-grid");
  if (!gridEl) return;

  const concertId = parseInt(gridEl.dataset.concertId, 10);
  const selectedSeats = new Set();

  const statusClass = {
    Available: "seat-available",
    PendingPayment: "seat-pending",
    Booked: "seat-booked"
  };

  function applySeatStatus(seatId, status) {
    const cell = gridEl.querySelector(`.seat-cell[data-seat-id="${seatId}"]`);
    if (!cell) return;

    cell.classList.remove("seat-available", "seat-pending", "seat-booked", "seat-selected");
    cell.classList.add(statusClass[status] || "seat-available");
    cell.dataset.status = status;

    if (status !== "Available") {
      selectedSeats.delete(seatId);
      updateHiddenInputs();
    }
  }

  function updateHiddenInputs() {
    const container = document.getElementById("selected-seat-inputs");
    container.innerHTML = "";
    selectedSeats.forEach((seatId) => {
      const input = document.createElement("input");
      input.type = "hidden";
      input.name = "SeatIds";
      input.value = seatId;
      container.appendChild(input);
    });

    const totalEl = document.getElementById("selected-count");
    if (totalEl) {
      totalEl.textContent = selectedSeats.size;
    }
  }

  gridEl.querySelectorAll(".seat-cell").forEach((cell) => {
    cell.addEventListener("click", () => {
      if (cell.dataset.status !== "Available") return;

      const seatId = parseInt(cell.dataset.seatId, 10);
      if (selectedSeats.has(seatId)) {
        selectedSeats.delete(seatId);
        cell.classList.remove("seat-selected");
      } else {
        selectedSeats.add(seatId);
        cell.classList.add("seat-selected");
      }
      updateHiddenInputs();
    });
  });

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/seat")
    .withAutomaticReconnect()
    .build();

  connection.on("SeatStatusChanged", ({ seatId, status }) => {
    applySeatStatus(seatId, status);
  });

  connection.onreconnected(() => {
    connection.invoke("JoinConcertGroup", concertId);
  });

  connection.start()
    .then(() => connection.invoke("JoinConcertGroup", concertId))
    .catch((err) => console.error("SignalR connection failed:", err));
})();
