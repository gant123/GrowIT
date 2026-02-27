import { expect, test } from '@playwright/test';

test.describe('Public beta smoke', () => {
  test('welcome page renders core messaging and demo CTA', async ({ page }) => {
    await page.goto('/welcome');

    await expect(page.getByText('Founder Survivability & Funding Readiness')).toBeVisible();
    await expect(page.getByRole('heading', { name: /Book A Demo Focused On Your Operating Model/i })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Request Demo' }).first()).toBeVisible();
  });

  test('anonymous user is redirected from protected home route', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/access-denied/);
    await expect(page.getByRole('heading', { name: /not authorized/i })).toBeVisible();
  });

  test('blog page loads', async ({ page }) => {
    await page.goto('/blog');

    await expect(page.getByRole('heading', { name: 'The grow.IT Blog' })).toBeVisible();
  });

  test('contact page accepts a demo submission', async ({ page }) => {
    await page.goto('/contact?demo=1&subject=Demo%20Request');

    await expect(page.getByText('Demo request mode is on. Confirm your details and send.')).toBeVisible();

    await page.getByLabel('Full Name').fill('Beta Smoke Tester');
    await page.getByLabel('Email').fill('beta-smoke@example.com');
    await page.getByLabel('Organization (Optional)').fill('GrowIT QA');
    await page.getByLabel('Message').fill('Requesting a beta readiness demo walkthrough.');

    await page.getByRole('button', { name: 'Send Message' }).click();

    await expect(page.getByText('Thanks, your message was sent. We will follow up shortly.')).toBeVisible();
  });
});
