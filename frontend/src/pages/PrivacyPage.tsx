export function PrivacyPage() {
  return (
    <article className="card prose">
      <h1>Polityka prywatności i ciasteczka</h1>
      <p>
        Car Rentals to projekt studencki. Zbieramy tylko dane potrzebne do wyceny i wynajmu auta. Nie sprzedajemy ich i
        nie używamy do reklam.
      </p>

      <h2>Jakie dane trzymamy</h2>
      <ul>
        <li>Imię, nazwisko i email z konta, którym się logujesz (Microsoft Entra ID).</li>
        <li>Datę urodzenia, datę uzyskania prawa jazdy i miasto, jeśli wpiszesz je w profilu. Na ich podstawie liczymy wiek i staż do wyceny.</li>
        <li>Wyceny (oferty) z wypożyczalni. Stare wyceny usuwamy automatycznie po 24 godzinach.</li>
        <li>Przy wynajmie (od KM3) przekazujemy wypożyczalni imię, nazwisko i email, bo bez tego nie przyjmie wniosku.</li>
      </ul>
      <p>
        Wyszukiwanie i wycena działają bez logowania. Wtedy wiek, staż i miasto idą tylko do wyceny i nie zapisujemy ich w
        profilu.
      </p>

      <h2>Jakich ciasteczek używamy</h2>
      <ul>
        <li>Ciasteczko sesji logowania (HttpOnly, Secure, SameSite=Lax). Bez niego nie da się być zalogowanym.</li>
        <li>Ciasteczka logowania Microsoft (tylko w trakcie logowania przez Entra ID).</li>
      </ul>
      <p>
        To są ciasteczka niezbędne, dlatego nie pytamy o zgodę. Nie mamy ciasteczek reklamowych ani analitycznych.
        Informację, że baner o ciasteczkach został zamknięty, zapisujemy w pamięci przeglądarki (localStorage), a nie w
        ciasteczku.
      </p>

      <h2>Bezpieczeństwo</h2>
      <p>
        Połączenie jest szyfrowane (HTTPS). W logach nie zapisujemy haseł, tokenów ani kluczy API, a dane osobowe są
        maskowane.
      </p>

      <h2>Twoje prawa</h2>
      <p>
        Dane w profilu możesz w każdej chwili zmienić albo wyczyścić na stronie Profil. Jeśli chcesz usunąć konto i
        wszystkie dane, napisz do zespołu projektu.
      </p>
    </article>
  )
}
