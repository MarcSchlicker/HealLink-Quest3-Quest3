package de.heallink.capture;

import android.app.Activity;
import android.content.Intent;
import android.media.projection.MediaProjectionManager;
import android.os.Bundle;

public final class CapturePermissionActivity extends Activity {
    private static final int CAPTURE_REQUEST = 2701;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        if (state == null) {
            MediaProjectionManager manager = getSystemService(MediaProjectionManager.class);
            startActivityForResult(manager.createScreenCaptureIntent(), CAPTURE_REQUEST);
        }
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (request != CAPTURE_REQUEST) return;
        if (result == RESULT_OK && data != null) {
            Intent service = new Intent(this, HeadsetCaptureService.class);
            service.putExtra("result", result);
            service.putExtra("permission", data);
            startForegroundService(service);
        } else {
            HeadsetCaptureService.setStatus("Screen sharing declined. Press record to try again.");
            HeadsetCaptureService.consentPending = false;
        }
        finish();
    }
}
