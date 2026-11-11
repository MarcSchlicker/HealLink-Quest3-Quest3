package de.heallink.capture;

import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.os.SystemClock;
import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;

public final class HeadsetCaptureService extends Service {
    private static volatile String status = "Screen sharing is stopped";
    public static volatile boolean consentPending;
    private static volatile boolean ready;
    private static byte[] latest;
    private MediaProjection projection;
    private VirtualDisplay display;
    private ImageReader reader;
    private HandlerThread thread;
    private long lastFrameTime;

    public static String getStatus() { return status; }
    public static boolean isReady() { return ready; }
    static void setStatus(String value) { status = value; }

    public static synchronized byte[] takeFrame() {
        byte[] frame = latest;
        latest = null;
        return frame;
    }

    private static synchronized void putFrame(byte[] frame) { latest = frame; }

    public static void request(Activity activity) {
        if (ready || consentPending) return;
        consentPending = true;
        status = "Confirm screen sharing in the headset";
        activity.runOnUiThread(() -> {
            try {
                activity.startActivity(new Intent(activity, CapturePermissionActivity.class));
            } catch (RuntimeException ex) {
                consentPending = false;
                status = "Could not open screen sharing permission";
            }
        });
    }

    public static void stop(Context context) {
        context.stopService(new Intent(context, HeadsetCaptureService.class));
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (projection != null) return START_NOT_STICKY;
        if (intent == null || !intent.hasExtra("permission")) {
            stopSelf();
            return START_NOT_STICKY;
        }
        try {
            NotificationManager notifications = getSystemService(NotificationManager.class);
            notifications.createNotificationChannel(new NotificationChannel(
                "heallink_capture", "Headset screen sharing", NotificationManager.IMPORTANCE_LOW));
            Notification notification = new Notification.Builder(this, "heallink_capture")
                .setSmallIcon(android.R.drawable.presence_video_online)
                .setContentTitle("HealLink screen sharing")
                .setContentText("Sharing the trainee headset view")
                .setOngoing(true).build();
            startForeground(2701, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);

            thread = new HandlerThread("HealLinkScreenCapture");
            thread.start();
            Handler handler = new Handler(thread.getLooper());
            MediaProjectionManager manager = getSystemService(MediaProjectionManager.class);
            projection = manager.getMediaProjection(intent.getIntExtra("result", Activity.RESULT_CANCELED),
                intent.getParcelableExtra("permission"));
            projection.registerCallback(new MediaProjection.Callback() {
                @Override public void onStop() {
                    status = "Screen sharing stopped by the headset";
                    stopSelf();
                }
            }, handler);

            final int width = 960, height = 720;
            reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 3);
            reader.setOnImageAvailableListener(source -> readImage(source), handler);
            display = projection.createVirtualDisplay("HealLinkTraineeView", width, height,
                getResources().getDisplayMetrics().densityDpi, DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                reader.getSurface(), null, handler);
            consentPending = false;
            status = "Waiting for the headset screen";
        } catch (RuntimeException ex) {
            status = "Headset screen capture could not start";
            consentPending = false;
            stopSelf();
        }
        return START_NOT_STICKY;
    }

    private void readImage(ImageReader source) {
        Bitmap padded = null;
        Bitmap bitmap = null;
        try (Image image = source.acquireLatestImage()) {
            if (image == null) return;
            long now = SystemClock.elapsedRealtime();
            if (now - lastFrameTime < 100) return;
            lastFrameTime = now;
            Image.Plane plane = image.getPlanes()[0];
            int stride = plane.getPixelStride();
            if (stride != 4) { status = "Unsupported headset image format"; return; }
            int paddedWidth = plane.getRowStride() / stride;
            if (paddedWidth < image.getWidth()) return;
            ByteBuffer buffer = plane.getBuffer();
            buffer.rewind();
            padded = Bitmap.createBitmap(paddedWidth, image.getHeight(), Bitmap.Config.ARGB_8888);
            padded.copyPixelsFromBuffer(buffer);
            bitmap = Bitmap.createBitmap(padded, 0, 0, image.getWidth(), image.getHeight());
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            if (bitmap.compress(Bitmap.CompressFormat.JPEG, 70, output)) {
                putFrame(output.toByteArray());
                ready = true;
                status = "Sharing the trainee headset view";
            }
        } catch (RuntimeException ex) {
            status = "Could not read the headset screen";
        } finally {
            if (bitmap != null && bitmap != padded) bitmap.recycle();
            if (padded != null) padded.recycle();
        }
    }

    @Override public void onDestroy() {
        ready = false;
        consentPending = false;
        putFrame(null);
        if (display != null) display.release();
        if (projection != null) projection.stop();
        if (reader != null) reader.close();
        if (thread != null) thread.quitSafely();
        stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
    }

    @Override public IBinder onBind(Intent intent) { return null; }
}
